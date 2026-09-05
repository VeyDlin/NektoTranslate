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
        ChapterImportResult result = await importer.ImportAsync(command.novelId, command.chapters, cancellationToken);

        return result.outcomes
            .Where(outcome => outcome.chapter is not null)
            .Select(outcome => outcome.chapter!)
            .ToList();
    }
}
