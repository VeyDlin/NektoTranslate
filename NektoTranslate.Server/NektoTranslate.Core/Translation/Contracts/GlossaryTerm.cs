namespace NektoTranslate.Translation.Contracts;


// A glossary row reduced to what the model needs. Keeps the translation engine independent of the
// glossary domain's storage.
public sealed record GlossaryTerm(
    string sourceTerm,
    string targetTerm,
    string? notes
);
