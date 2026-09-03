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

    public async ValueTask<IReadOnlyList<Chapter>> Handle(
        ImportChaptersCommand command,
        CancellationToken cancellationToken
    ) {
        return await importer.ImportAsync(command.novelId, command.chapters, cancellationToken);
    }
}
