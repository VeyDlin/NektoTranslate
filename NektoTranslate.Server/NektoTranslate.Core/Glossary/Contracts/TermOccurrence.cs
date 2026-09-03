namespace NektoTranslate.Glossary.Contracts;


// Where a term was found on the source side, down to the paragraph. Paragraph granularity is what
// makes the lookup cheap: only that paragraph and its counterpart go to the model, not the chapter.
public sealed record TermOccurrence(
    long chapterId,
    int chapterIndex,
    int segmentIndex,
    string segmentText
);
