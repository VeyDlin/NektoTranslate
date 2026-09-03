namespace NektoTranslate.Settings.Contracts;


// Every field optional: null means "leave alone". Lets the interface save one field without
// echoing back the rest, and stops a stale form from overwriting a change made elsewhere.
//
// Numeric values are clamped to a usable range on write rather than validated with an error. A
// setting screen should not be able to put the engine into a state where it cannot translate, and
// silently correcting an out-of-range number is friendlier than refusing the whole save.
public sealed record UpdateSettingsRequest(
    string? globalStyleGuide = null,
    string? defaultModel = null,
    string? glossaryModel = null,
    string? localModelEndpoint = null,
    string? localModelName = null,
    string? localModelApiKey = null,
    int? maxOutputTokens = null,
    double? expansionFactor = null,
    int? voiceWindowChapters = null,
    int? voiceWindowParagraphs = null,
    int? pageLoadTimeoutMs = null,
    int? chatMaxRounds = null
);
