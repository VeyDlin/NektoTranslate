using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using NektoTranslate.Common.Data;
using NektoTranslate.Common.Extensions;
using NektoTranslate.Common.Http;
using NektoTranslate.Common.Models;
using NektoTranslate.Common.Services;
using NektoTranslate.Translation.Hubs;


WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// The console is the log. The framework's defaults also register the Windows Event Log, which would
// copy every warning of a single-user desktop tool into the machine's Application log, and that
// provider is the one that throws when the host fails to start and something still tries to log.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// A single-user local application: the database, the downloaded browser and everything else this
// process owns live beside the user's other application data, not in the build output, so they
// survive a rebuild and a reinstall. See DataPaths for where that is on each platform.
//
// Overridable for the cases the default cannot serve - a library kept on another drive, or two
// installations that should not share one database.
DataPaths.Layout dataPaths = DataPaths.Resolve(builder.Configuration["DataDirectory"]);

Directory.CreateDirectory(dataPaths.root);
Directory.CreateDirectory(dataPaths.browsers);
Directory.CreateDirectory(dataPaths.logs);

// Enums go out as names, not ordinals. SignalR already sent names, so numbers over REST left the
// frontend translating between two representations of the same value - and a reordered enum would
// have silently changed the meaning of stored-looking numbers on the wire.
builder.Services
    .AddControllers()
    .AddJsonOptions(options => {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());

        // Entities carry navigation properties back to their parents, and the parents carry
        // collections back to them. Serialising one loops until the serializer gives up, which
        // surfaces as a 500 on an endpoint that was working the day before someone added a
        // navigation property. Endpoints should project, and this makes the failure a missing field
        // rather than a broken response when one forgets.
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    });

builder.Services.AddSignalR();

// A request that fails answers with a status the interface can show, never with an empty 500. See
// StatusExceptionHandler for what goes out and why. ProblemDetails stays registered as the fallback
// the framework insists on, though the handler answers everything itself.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<StatusExceptionHandler>();
// Handlers reach for the DbContext, which is scoped. Mediator registers them as singletons unless
// told otherwise, and a singleton holding a scoped context is both a lifetime violation and a
// connection that never gets released.
builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
// Parser scripts ship with the application but stay loose on disk, so a site fix is an edit rather
// than a rebuild.
string parserDirectory = Path.Combine(AppContext.BaseDirectory, "parsers");

// Internal tuning. Every field defaults in code, so an absent or partial "Engine" section is not a
// missing configuration - it is the ordinary case, and it behaves exactly as the constants it
// replaced. What the user changes from the interface lives in the database instead.
EngineOptions engine = builder.Configuration.GetSection("Engine").Get<EngineOptions>()
    ?? new EngineOptions();

builder.Services.AddNektoTranslate(
    dataPaths,
    parserDirectory,
    engine
);

// Nothing here is meant to be reachable from the network. There is no authentication because there
// is no second user; binding to the loopback interface is what keeps that assumption true.
//
// Configurable only so a taken port can be moved. Changing the host away from loopback removes the
// only thing standing between an unauthenticated application and the network, which is why the
// default is spelled out rather than left to the framework.
string serverUrl = builder.Configuration["ServerUrl"] ?? "http://127.0.0.1:5080";

builder.WebHost.UseUrls(serverUrl);

// A desktop shell that spawned this process passes its own pid so this one can notice when the
// shell is gone and stop itself - the case that would otherwise leave a server running with no
// window left to close it. Absent for every other way this process is started, so registration
// below is conditional rather than part of every run.
int? parentPid = int.TryParse(builder.Configuration["ParentPid"], out int parsedParentPid)
    ? parsedParentPid
    : null;

if (parentPid is not null) {
    builder.Services.AddHostedService(provider => new ParentWatchdog(
        parentPid.Value,
        provider.GetRequiredService<IHostApplicationLifetime>(),
        provider.GetRequiredService<ILogger<ParentWatchdog>>()
    ));
}

WebApplication app = builder.Build();

// Asked before the host starts, and before the migrations below touch the database: a second copy
// started while the first is still up is the one startup failure a person is likely to meet, and
// the framework's answer to it is a page of stack trace logged twice. One line, straight to the
// console - the host's own loggers are not safe to use once its start has failed.
if (!PortIsFree(serverUrl)) {
    Console.Error.WriteLine(
        $"{serverUrl} is already taken, most likely by another NektoTranslate. Stop it, or start this "
        + "one with --ServerUrl=http://127.0.0.1:<another port>."
    );

    return 1;
}

using (IServiceScope scope = app.Services.CreateScope()) {
    scope.ServiceProvider.GetRequiredService<NektoDbContext>().Database.Migrate();
}

// The two lines a person actually needs when the application is not where they expect it, or is
// not answering on the port they expected. Logged under a category of its own, at Information, so
// they survive a production log level that otherwise drops almost everything below a warning.
ILogger startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("NektoTranslate");

string version = Assembly.GetEntryAssembly()
    ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
    ?.InformationalVersion
    ?? "unknown";

startupLogger.LogInformation("NektoTranslate {Version}", version);
startupLogger.LogInformation("Data directory: {DataDirectory}", dataPaths.root);
startupLogger.LogInformation("Listening on {ServerUrl}", serverUrl);

app.UseExceptionHandler();

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions {
    OnPrepareResponse = context => {
        // Vite hashes every file under assets/ by content, so the same URL never means two
        // different files across a release - a long cache is free. Everything else, index.html
        // included, keeps the same name release to release and has to be revalidated every time.
        bool hashedAsset = context.Context.Request.Path.StartsWithSegments("/assets");

        context.Context.Response.Headers.CacheControl = hashedAsset
            ? "public, max-age=31536000, immutable"
            : "no-cache";
    }
});

// The client's own router owns paths like /novels/12/read/340 - there is no file by that name and
// none was ever meant to exist on disk. Only a GET that reaches here with nothing above having
// answered it, and that is not for /api or /hubs, is treated as one of those: an unmatched API
// route or hub path still 404s exactly as it did before wwwroot existed, rather than getting the
// page. Placed before the endpoints below so its `next()` wraps routing and their execution.
app.Use(async (context, next) => {
    await next(context);

    bool unresolvedClientRoute = context.Response.StatusCode == StatusCodes.Status404NotFound
        && !context.Response.HasStarted
        && context.Request.Method == HttpMethods.Get
        && !context.Request.Path.StartsWithSegments("/api")
        && !context.Request.Path.StartsWithSegments("/hubs");

    if (!unresolvedClientRoute) {
        return;
    }

    string indexPath = Path.Combine(app.Environment.WebRootPath ?? string.Empty, "index.html");

    if (!File.Exists(indexPath)) {
        // A Debug run from the IDE has no wwwroot at all - nothing changes for it, and / still
        // 404s exactly as it always has.
        return;
    }

    context.Response.StatusCode = StatusCodes.Status200OK;
    context.Response.ContentType = "text/html";
    context.Response.Headers.CacheControl = "no-cache";
    await context.Response.SendFileAsync(indexPath, context.RequestAborted);
});

app.MapControllers();
app.MapHub<TranslationHub>("/hubs/translation");

app.Run();

return 0;


// A bind-and-release on the exact address the host is about to take. There is a moment between
// this and the real bind in which another process could still win the port, but that is a race
// between two copies started in the same instant, not the case this guards against.
static bool PortIsFree(string serverUrl) {
    Uri address = new Uri(serverUrl);
    IPAddress host = IPAddress.TryParse(address.Host, out IPAddress? parsed) ? parsed : IPAddress.Loopback;
    TcpListener probe = new TcpListener(host, address.Port);

    try {
        probe.Start();

        return true;
    } catch (SocketException) {
        return false;
    } finally {
        probe.Stop();
    }
}
