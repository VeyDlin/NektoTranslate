namespace NektoTranslate.Common.Contracts;


// The single store of everything the server can say.
//
// One place rather than strings scattered through services and controllers, for the reason a
// translation file needs: the set of things that can be said has to be enumerable. A translator
// given this file has the whole job in front of them; a translator given a codebase does not.
//
// Members are grouped by the screen that shows them rather than by the service that raises them,
// because that is how a person looks them up.
public static class Statuses {

    // --- importing an existing translation ---------------------------------------------------

    public static readonly Status NoChapterAtIndex = new(
        "NO_CHAPTER_AT_INDEX",
        "There is no chapter {index}. Import the original first, or shift the mapping."
    );

    public static readonly Status TranslationAlreadyExists = new(
        "TRANSLATION_ALREADY_EXISTS",
        "Chapter {index} already has a {language} translation. Delete it first if it should be replaced."
    );

    public static readonly Status ImportedTextEmpty = new(
        "IMPORTED_TEXT_EMPTY",
        "The text for chapter {index} was empty."
    );

    public static readonly Status ChapterFetchFailed = new(
        "CHAPTER_FETCH_FAILED",
        "Could not read {url}: {reason}"
    );

    public static readonly Status ParserFoundNoContent = new(
        "PARSER_FOUND_NO_CONTENT",
        "The page at {url} has no chapter text the parser recognises."
    );

    public static readonly Status NoParserForSite = new(
        "NO_PARSER_FOR_SITE",
        "No parser claims {url}."
    );

    public static readonly Status ImportCancelled = new(
        "IMPORT_CANCELLED",
        "The import was cancelled before this chapter was reached."
    );

    // --- retrying one item of a settled import ---------------------------------------------------

    public static readonly Status ImportJobStillActive = new(
        "IMPORT_JOB_STILL_ACTIVE",
        "This import is still running. Wait for it to finish, then retry the chapter."
    );

    public static readonly Status ImportItemNotFailed = new(
        "IMPORT_ITEM_NOT_FAILED",
        "This chapter did not fail, so there is nothing to retry."
    );

    // --- moving translations between chapters --------------------------------------------------

    public static readonly Status MoveTargetMissing = new(
        "MOVE_TARGET_MISSING",
        "There is no chapter at position {target}."
    );

    public static readonly Status MoveTargetOccupied = new(
        "MOVE_TARGET_OCCUPIED",
        "Chapter {target} already has a translation that is not part of this move."
    );

    // --- editing a block ------------------------------------------------------------------------

    public static readonly Status ChapterBusy = new(
        "CHAPTER_BUSY",
        "This chapter is queued or being translated. The run would overwrite the edit."
    );

    public static readonly Status TranslationStale = new(
        "TRANSLATION_STALE",
        "The chapter has been translated again since this edit was started. Reload it and make the change on the current text."
    );

    public static readonly Status BlockOutOfRange = new(
        "BLOCK_OUT_OF_RANGE",
        "That block does not exist in this translation."
    );

    public static readonly Status NoTranslationInLanguage = new(
        "NO_TRANSLATION_IN_LANGUAGE",
        "This chapter has no {language} translation yet."
    );

    // --- quality findings -----------------------------------------------------------------------

    public static readonly Status BlockUnchanged = new(
        "BLOCK_UNCHANGED",
        "This block came back unchanged from the source."
    );

    public static readonly Status SourceScriptResidue = new(
        "SOURCE_SCRIPT_RESIDUE",
        "A run of source-language text survived into the translation: \"{run}\""
    );

    public static readonly Status GlossaryTermIgnored = new(
        "GLOSSARY_TERM_IGNORED",
        "The established rendering \"{source}\" -> \"{target}\" was not used."
    );

    // --- models ---------------------------------------------------------------------------------

    public static readonly Status LocalModelNotConfigured = new(
        "LOCAL_MODEL_NOT_CONFIGURED",
        "No local model endpoint is configured."
    );

    public static readonly Status LocalModelTimedOut = new(
        "LOCAL_MODEL_TIMED_OUT",
        "The local model server did not answer in time."
    );

    public static readonly Status LocalModelBadResponse = new(
        "LOCAL_MODEL_BAD_RESPONSE",
        "{url} answered {status}."
    );

    public static readonly Status LocalModelListFailed = new(
        "LOCAL_MODEL_LIST_FAILED",
        "Listing the local models failed: {reason}"
    );

    public static readonly Status ModelRejected = new(
        "MODEL_REJECTED",
        "The model was rejected: {reason}"
    );

    public static readonly Status ModelRejectedWithoutReason = new(
        "MODEL_REJECTED_WITHOUT_REASON",
        "The model was rejected, and the CLI gave no reason."
    );
}
