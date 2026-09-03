using NektoTranslate.Glossary.Enums;


namespace NektoTranslate.Glossary.Contracts;


public sealed record UpsertGlossaryEntryRequest(
    string language,
    string sourceTerm,
    string targetTerm,
    GlossaryCategory? category = null,
    string? notes = null,
    IReadOnlyList<string>? aliases = null
);
