using NektoTranslate.Common.Contracts;


namespace NektoTranslate.Translation.Contracts;


// One chapter of an existing translation, and which original chapter it belongs to.
//
// The mapping is carried per entry rather than inferred from position in the list, because position
// is exactly what goes wrong: a translation site that opens with a translator's note has every
// entry one place out, and a caller that could only say "these in order" would have no way to say
// otherwise. The offset is the caller's to decide, and to correct afterwards.
public sealed record ImportedTranslation(
    int chapterIndex,
    string html,
    string? sourceUrl = null,
    // Only used when the entry lands on a chapter that does not exist yet and the caller allowed one
    // to be created. A chapter made this way has no original to take a title from, so the
    // translation's own is the only one there is.
    string? title = null
);


// Why an entry did not land. Reported rather than thrown: a hundred-chapter import that fails
// wholesale because one entry had nowhere to go is worse than one that lands ninety-nine and says
// which one did not.
public sealed record TranslationImportRejection(
    int chapterIndex,
    Status reason
);


public sealed record TranslationImportResult(
    int imported,
    IReadOnlyList<TranslationImportRejection> rejected,
    // How many of the imported entries had to have a chapter made for them. Reported separately
    // because it is the one outcome the user has to be able to see coming: a mis-set offset that
    // would have been a hundred rejections is now a hundred new chapters.
    int createdChapters = 0
);


public sealed record ImportTranslationRequest(
    string language,
    IReadOnlyList<ImportedTranslation> translations,
    bool createMissingChapters = false
);
