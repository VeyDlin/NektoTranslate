using Mediator;
using NektoTranslate.Common.Data;
using NektoTranslate.Novels.Contracts;
using NektoTranslate.Novels.Entities;
using NektoTranslate.Settings.Services;


namespace NektoTranslate.Novels.Commands;


public record CreateNovelCommand(CreateNovelRequest request) : ICommand<Novel>;


public class CreateNovelCommandHandler(
    NektoDbContext database,
    ISettingsService settings
) : ICommandHandler<CreateNovelCommand, Novel> {

    public async ValueTask<Novel> Handle(CreateNovelCommand command, CancellationToken cancellationToken) {
        // The application-wide default is applied here rather than left to the entity's own, so
        // changing it in settings affects the next book created instead of only books created
        // before someone edited a constant.
        string model = command.request.model
            ?? (await settings.GetAsync(cancellationToken)).defaultModel;

        Novel novel = new Novel {
            title = command.request.title,
            sourceLanguage = command.request.sourceLanguage,
            targetLanguage = command.request.targetLanguage,
            sourceUrl = command.request.sourceUrl,
            styleGuide = command.request.styleGuide,
            model = model
        };

        database.novels.Add(novel);
        await database.SaveChangesAsync(cancellationToken);

        return novel;
    }
}
