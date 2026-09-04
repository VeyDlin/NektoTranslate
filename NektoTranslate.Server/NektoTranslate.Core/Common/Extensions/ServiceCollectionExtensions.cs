using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NektoTranslate.Chapters.Services;
using NektoTranslate.Chat.Services;
using NektoTranslate.Common.Data;
using NektoTranslate.Common.Models;
using NektoTranslate.Common.Tools;
using NektoTranslate.Glossary.Services;
using NektoTranslate.Glossary.Tools;
using NektoTranslate.Jobs.Services;
using NektoTranslate.Parsing.Services;
using NektoTranslate.Settings.Services;
using NektoTranslate.Translation.Checks;
using NektoTranslate.Translation.Services;


namespace NektoTranslate.Common.Extensions;


public static class ServiceCollectionExtensions {

    // Configuration arrives in two forms and the split is deliberate. EngineOptions is the internal
    // tuning read from appsettings.json, fixed for the life of the process; everything the user
    // changes from the interface lives in the database and is read per request, so that starting a
    // local model or raising the output ceiling takes effect without a restart.
    public static IServiceCollection AddNektoTranslate(
        this IServiceCollection services,
        string databasePath,
        string parserDirectory,
        EngineOptions? engine = null
    ) {
        services.AddSingleton(engine ?? new EngineOptions());

        services.AddDbContext<NektoDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));

        services.AddScoped<ISettingsService, SettingsService>();

        // Scoped rather than singleton because the endpoint now comes from the database: a factory
        // that captured it at startup would keep pointing at a server the user has since changed.
        services.AddScoped<IChatClientFactory, ChatClientFactory>();

        services.AddHttpClient();
        services.AddScoped<IModelCatalog, ModelCatalog>();

        services.AddSingleton<IChapterHtmlSanitizer, ChapterHtmlSanitizer>();
        services.AddSingleton<IChapterSegmenter, ChapterSegmenter>();
        // One conversion for both sides of a book. Sharing it is what keeps an imported translation
        // comparable, paragraph for paragraph, with the original it is attached to.
        services.AddSingleton<IMarkdownConversion, MarkdownConversion>();
        services.AddScoped<IChapterImportService, ChapterImportService>();

        // The Claude-backed services hold no state between calls - each one spawns its own CLI
        // process - so a single instance serves every request.
        services.AddSingleton<ITranslator>(_ => new ClaudeTranslator());
        services.AddSingleton<ISegmentTranslator, ClaudeSegmentTranslator>();

        // The glossary's small calls go to a local model when one is configured, and fall back to
        // the subscription when it is absent or unreachable. Chapter translation is not wired this
        // way on purpose: prose is where model quality shows, and the mechanical calls are the
        // numerous ones, so this is where moving work off the subscription pays.
        //
        // Scoped, following the chat client factory it depends on.
        services.AddScoped<ITermExtractor>(provider => new LocalTermExtractor(
            provider.GetRequiredService<IChatClientFactory>(),
            new ClaudeTermExtractor(provider.GetRequiredService<EngineOptions>()),
            provider.GetRequiredService<EngineOptions>()
        ));

        services.AddScoped<IChapterTranslator, ChapterTranslator>();
        services.AddScoped<ITranslationEditor, TranslationEditor>();
        services.AddScoped<ITranslationImportService, TranslationImportService>();
        services.AddScoped<ITranslationMapping, TranslationMapping>();
        services.AddScoped<IChapterTranslationStateSync, ChapterTranslationStateSync>();
        services.AddScoped<ITranslationNotifier, SignalRTranslationNotifier>();

        services.AddScoped<ITermLocator, ExactTermLocator>();
        services.AddScoped<IExistingTranslationResolver, ExistingTranslationResolver>();
        services.AddSingleton<IGlossaryUsageChecker, GlossaryUsageChecker>();

        // Post-translation quality checks. Collected through the shared contract so adding one is
        // adding a class, and so each can declare for itself which language pairs it applies to -
        // which is what stops a rule written for one script being applied to every language.
        services.AddSingleton<ITranslationCheck, GlossaryUsageCheck>();
        services.AddSingleton<ITranslationCheck, UntranslatedResidueCheck>();
        services.AddSingleton<ITranslationCheck, UntranslatedBlockCheck>();
        services.AddSingleton<ITranslationCheckRunner, TranslationCheckRunner>();
        services.AddScoped<IGlossaryService, GlossaryService>();

        // Agent tools. Registered against the shared IAgentTool contract so the registry picks up
        // anything added later without being edited, and so an MCP transport added on top only has
        // to walk the registry. They reach for the database, hence scoped.
        services.AddScoped<IAgentTool, GlossaryLookupTool>();
        services.AddScoped<IAgentTool, GlossarySearchTool>();
        services.AddScoped<IAgentTool, GlossaryRecordTool>();
        services.AddScoped<IAgentToolRegistry, AgentToolRegistry>();

        // Site parsing. The browser is expensive to launch and stateless between novels, so one is
        // shared for the life of the application. Everything above it is scoped, because the parser
        // set depends on the user's stored edits and so has to be read per request.
        services.AddSingleton<IBrowserSession, PlaywrightBrowserSession>();
        // Singleton, and it has to be: limits shared by nothing are not limits. Two scoped
        // schedulers would each allow the full quota and the site would see twice the traffic.
        services.AddSingleton<IPageScheduler, PageScheduler>();
        services.AddSingleton(_ => new FileParserScriptStore(parserDirectory));
        services.AddScoped<IParserScriptStore, MergedParserScriptStore>();
        services.AddScoped<ISiteParser, WebToEpubRunner>();

        // The agent chat. Reaches the same tool registry the rest of the application uses, so a
        // correction made in conversation lands in the glossary rather than only in the transcript.
        services.AddScoped<IChatAgentService, ChatAgentService>();

        services.AddSingleton<TranslationJobQueue>();
        services.AddScoped<ITranslationJobService, TranslationJobService>();
        services.AddHostedService<TranslationJobWorker>();

        // Imports run on their own queue and worker: they wait on websites, translations wait on a
        // model, and neither should hold the other up.
        services.AddSingleton<ImportJobQueue>();
        services.AddScoped<IImportJobService, ImportJobService>();
        services.AddHostedService<ImportJobWorker>();

        return services;
    }
}
