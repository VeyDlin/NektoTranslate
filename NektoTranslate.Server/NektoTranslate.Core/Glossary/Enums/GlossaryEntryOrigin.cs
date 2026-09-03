namespace NektoTranslate.Glossary.Enums;


public enum GlossaryEntryOrigin {
    // The model chose this rendering itself; nothing established it beforehand.
    AiExtracted = 0,

    // Recovered from a translation that already existed in the book, so it matches what the
    // reader has already seen.
    FromExistingTranslation = 1,

    Manual = 2
}
