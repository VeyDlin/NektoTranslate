namespace NektoTranslate.Jobs.Enums;


public enum ImportKind {

    // Chapters of the original, creating Chapter rows.
    Originals = 0,

    // An existing translation, attached to chapters that are already there.
    Translation = 1
}
