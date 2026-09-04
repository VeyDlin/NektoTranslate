using NektoTranslate.Common.Contracts;


namespace NektoTranslate.Translation.Contracts;


// Moves the translations of one span of chapters onto a different span.
//
// Expressed as a span plus an offset rather than as a list of pairs, because that is the shape the
// interface actually produces: a user selects chapters 20 to 120 and drags them down two places.
// Sending a hundred individual moves would make a drag cost a hundred round trips and give the
// server no way to see that they were meant as one thing.
public sealed record MoveTranslationsRequest(
    string language,
    int fromIndex,
    int toIndex,
    int offset,
    // A dry run answers "what would this do" without doing it, so drag-and-drop can show the
    // collision before the user drops rather than after. Same request, same answer, nothing written.
    bool apply = true
);


public sealed record TranslationCollision(
    int fromIndex,
    int targetIndex,
    Status reason
);


public sealed record MoveTranslationsResult(
    bool applied,
    int moved,
    IReadOnlyList<TranslationCollision> collisions
);


public sealed record DeleteTranslationsResult(
    int chapters,
    int versions
);
