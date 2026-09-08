using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using NektoTranslate.Common.Data;
using NektoTranslate.Common.Extensions;
using NektoTranslate.Common.Http;
using NektoTranslate.Common.Models;
using NektoTranslate.Common.Services;
using NektoTranslate.Translation.Hubs;


WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

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

WebApplication app = builder.Build();

using (IServiceScope scope = app.Services.CreateScope()) {
    scope.ServiceProvider.GetRequiredService<NektoDbContext>().Database.Migrate();
}

// The two lines a person actually needs when the application is not where they expect it, or is
// not answering on the port they expected. Logged under a category of its own, at Information, so
// they survive a production log level that otherwise drops almost everything below a warning.
ILogger startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("NektoTranslate");

startupLogger.LogInformation("Data directory: {DataDirectory}", dataPaths.root);
startupLogger.LogInformation("Listening on {ServerUrl}", serverUrl);

app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();
app.MapHub<TranslationHub>("/hubs/translation");

app.Run();
