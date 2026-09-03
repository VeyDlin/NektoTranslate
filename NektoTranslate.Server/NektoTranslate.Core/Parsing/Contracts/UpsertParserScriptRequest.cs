namespace NektoTranslate.Parsing.Contracts;


public sealed record UpsertParserScriptRequest(
    string scriptSource,
    string? displayName = null,
    bool? enabled = null
);
