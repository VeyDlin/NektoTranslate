namespace NektoTranslate.Jobs.Enums;


// The state of one chapter within an import, which is the unit a person actually wants reported.
// "3 imported, 38 failed" is a summary; the list underneath is these.
public enum ImportItemState {

    Pending = 0,

    Imported = 1,

    // Nothing was written and nothing went wrong - the chapter already had a translation in this
    // language, and the import does not overwrite.
    Skipped = 2,

    Failed = 3,

    // The run was cancelled before this one was reached.
    Cancelled = 4
}
