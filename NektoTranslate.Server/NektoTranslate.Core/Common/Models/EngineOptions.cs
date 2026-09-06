using NektoTranslate.Settings.Contracts;


namespace NektoTranslate.Common.Models;


// Internal tuning, read from appsettings.json and defaulted here.
//
// Three tiers of configuration, and the difference matters:
//
//   code defaults   - always valid, always present, need no file
//   appsettings     - these; advanced knobs for someone who knows what they are changing
//   database        - ApplicationSettings; the trade-offs a user makes from the interface
//
// What lives here rather than in the admin screen is anything with essentially one right answer,
// where a field in the interface would mostly invite breaking what works. It is still reachable
// for an installation that genuinely needs it - a slow site, an unusual model - without a rebuild.
//
// Every value defaults to what the code used before it was configurable, so an empty or absent
// "Engine" section changes nothing.
public sealed record EngineOptions {

    public BatchingOptions batching { get; init; } = new();

    public GlossaryOptions glossary { get; init; } = new();

    public ChecksOptions checks { get; init; } = new();

    public ChatAgentOptions chat { get; init; } = new();

    public BrowserOptions browser { get; init; } = new();

    // The models offered in the interface. Configurable because the CLI has no way to be asked -
    // there is no subcommand that prints its catalogue, and an unknown name is only discovered by
    // being rejected. Overriding this section is how a user reaches a model shipped after this
    // build, without waiting for a new one.
    //
    // Deliberately empty here rather than defaulting to DefaultModels. Configuration binding does
    // not replace a collection it finds already populated - it appends to it - so a default list
    // plus the same list in appsettings.json showed every model twice. Whoever reads this list
    // substitutes DefaultModels when it is empty.
    public IReadOnlyList<ModelOption> models { get; init; } = [];


    // Aliases rather than pinned names, deliberately. An alias tracks the newest model of its
    // family, so this list stays correct as models are released; a list of dated names would start
    // ageing the day it was written and would silently keep using a superseded model.
    public static IReadOnlyList<ModelOption> DefaultModels { get; } = [
        new ModelOption(
            "sonnet",
            "Sonnet",
            "The working default. Strong literary judgement at a fraction of Opus's cost, and what "
            + "a full-length book is realistically translated with.",
            true
        ),
        new ModelOption(
            "opus",
            "Opus",
            "The most capable, and by a wide margin the most expensive. Worth it for a difficult "
            + "passage or a book whose prose carries it; wasteful across thousands of pages.",
            true
        ),
        new ModelOption(
            "haiku",
            "Haiku",
            "Fast and cheap. Suited to the glossary's mechanical work - listing proper nouns, "
            + "reading back how a term was rendered - rather than to prose.",
            true
        ),
        new ModelOption(
            "fable",
            "Fable",
            "Tuned for creative writing, which is closer to what a novel translation actually is "
            + "than general assistance.",
            true
        )
    ];
}


public sealed record BatchingOptions {

    // Secondary guard alongside the token budget. Characters are a poor proxy for tokens across
    // scripts, but they bound a request when the token estimate is wrong.
    public int maxCharacters { get; init; } = 12_000;

    // Held back from the output ceiling so a reply that runs slightly long still lands.
    public int safetyMargin { get; init; } = 500;

    // However the arithmetic comes out, never below this: a budget of a few hundred tokens turns
    // one chapter into hundreds of requests, each paying for the system prompt again.
    public int minimumTokens { get; init; } = 1_000;

    // Attempts at the segment format before a batch is subdivided.
    public int maxAttempts { get; init; } = 2;

    // How much of the previous batch's translation is carried forward as voice within a chapter.
    public int carryParagraphs { get; init; } = 3;
}


public sealed record GlossaryOptions {

    // How many chapters are examined when looking for a term in an existing translation.
    public int maxCandidateChapters { get; init; } = 5;

    // How far to look either side when the two sides do not have the same paragraph count.
    public int alignmentWindow { get; init; } = 2;

    // Letters that may follow a term and still count as the same word. Three covers the case
    // endings of Russian, Polish, Czech and German without swallowing compounds; a language with
    // longer inflections would want more.
    public int maxInflectionLength { get; init; } = 3;

    // A proper noun is short. Anything longer is a model explaining itself instead of listing.
    public int maxTermLength { get; init; } = 80;

    // A misspelled name is the one thing a reader notices first, so a repair offers every Person,
    // Place and Organization term regardless of whether the chapter's own text mentions it - that is
    // exactly the case the mention test cannot catch. Uncapped, a book with a large cast would grow
    // every repair request by its whole roster of names instead of by the chapter; 150 comfortably
    // covers a normal cast while still bounding the worst case.
    public int maxNamesPerRepair { get; init; } = 150;
}


public sealed record ChecksOptions {

    // Consecutive source-script letters in the translation before it counts as a skipped fragment
    // rather than a name left in its original spelling.
    public int minimumResidueRun { get; init; } = 4;

    // Length below which an identical line is not evidence of a copied block - a name on its own,
    // a number, an interjection.
    public int minimumBlockLength { get; init; } = 12;

    public int maxReported { get; init; } = 5;
}


// How hard the parsers are allowed to lean on the sites they read.
//
// This matters more than a usual throughput setting, because the load does not fall on us. A book
// with two thousand chapters is two thousand page loads against someone else's server, and the
// polite version of that is the only version worth shipping: a reader who gets the host's IP
// blocked has lost the book, not gained speed.
//
// Three separate limits, because they answer three different questions. The global cap is about
// this machine - every open page is a real Chromium tab with its own memory. Per-host concurrency
// and the interval between requests are about the site, and are the ones that keep a long import
// from looking like an attack.
public sealed record BrowserOptions {

    // Pages open at once across every site. Chromium tabs are not cheap, and an import that opens
    // forty at once will run out of memory long before it runs out of chapters.
    public int maxConcurrentPages { get; init; } = 3;

    // Pages open at once against a single site. One is deliberate: two sites are fetched in
    // parallel, one site is fetched in order, and a second request for a host that is already busy
    // waits its turn instead of doubling the load.
    public int perHostConcurrency { get; init; } = 1;

    // Minimum gap between the start of one request to a host and the next.
    //
    // Measured from start rather than from finish, so a slow page does not earn an extra pause on
    // top of the time it already took. Parsers often pace themselves internally; this is the floor
    // underneath whatever they do, and it applies to the ones that do not.
    public int minHostIntervalMs { get; init; } = 1_000;
}


// Named for the agent rather than plainly "ChatOptions", because Microsoft.Extensions.AI already
// has that name and the two meet in the same file wherever a local model is called.
public sealed record ChatAgentOptions {

    // How much of the conversation is replayed to the agent each turn.
    public int historyTurns { get; init; } = 12;
}
