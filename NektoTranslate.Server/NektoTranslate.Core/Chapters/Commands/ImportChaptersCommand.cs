using Mediator;
using NektoTranslate.Chapters.Contracts;
using NektoTranslate.Chapters.Entities;
using NektoTranslate.Chapters.Services;


namespace NektoTranslate.Chapters.Commands;


public record ImportChaptersCommand(
    long novelId,
    IReadOnlyList<ImportedChapter> chapters
) : ICommand<IReadOnlyList<Chapter>>;


public class ImportChaptersCommandHandler(IChapterImportService importer)
    : ICommandHandler<ImportChaptersCommand, IReadOnlyList<Chapter>> {

    // The paste endpoint this command serves has never had to say why an entry did not land - it
    // predates addresses entirely, and nothing on that screen reads a rejection today. So the outcome
    // is flattened to the chapters that landed, same as before; an already-imported entry is skipped
    // the same as a freshly created one is included, both by way of outcome.chapter.
    public async ValueTask<IReadOnlyList<Chapter>> Handle(
        ImportChaptersCommand command,
        CancellationToken cancellationToken
    ) {
        // Pasted content has no site behind it to have changed, so there is nothing for it to
        // replace - only a fetched re-import of an address the book already holds ever sets this.
        ChapterImportResult result = await importer.ImportAsync(
            command.novelId,
            command.chapters,
            replaceExisting: false,
            cancellationToken
        );

        return result.outcomes
            .Where(outcome => outcome.chapter is not null)
            .Select(outcome => outcome.chapter!)
            .ToList();
    }
}
