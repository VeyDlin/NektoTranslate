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
    string? sourceUrl = null
);


// Why an entry did not land. Reported rather than thrown: a hundred-chapter import that fails
// wholesale because one entry had nowhere to go is worse than one that lands ninety-nine and says
// which one did not.
public sealed record TranslationImportRejection(
    int chapterIndex,
    string reason
);


public sealed record TranslationImportResult(
    int imported,
    IReadOnlyList<TranslationImportRejection> rejected
);


public sealed record ImportTranslationRequest(
    string language,
    IReadOnlyList<ImportedTranslation> translations
);
