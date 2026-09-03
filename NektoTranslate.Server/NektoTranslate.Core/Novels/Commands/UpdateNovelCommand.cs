using Mediator;
using Microsoft.EntityFrameworkCore;
using NektoTranslate.Common.Data;
using NektoTranslate.Novels.Contracts;
using NektoTranslate.Novels.Entities;


namespace NektoTranslate.Novels.Commands;


public record UpdateNovelCommand(long novelId, UpdateNovelRequest request) : ICommand<Novel?>;


// Edits a novel's settings after creation.
//
// Every field here is something the user only knows they want once they have read some of the
// translation - the book's own style prompt above all, which is the whole point of having one. A
// setting that can only be chosen before the first chapter exists is a setting chosen blind.
//
// Null means "leave alone", so the caller can change one field without echoing back the others and
// without racing another edit. An empty string clears a nullable field deliberately.
public class UpdateNovelCommandHandler(NektoDbContext database) : ICommandHandler<UpdateNovelCommand, Novel?> {

    public async ValueTask<Novel?> Handle(UpdateNovelCommand command, CancellationToken cancellationToken) {
        Novel? novel = await database.novels.FirstOrDefaultAsync(
            candidate => candidate.id == command.novelId,
            cancellationToken
        );

        if (novel is null) {
            return null;
        }

        UpdateNovelRequest request = command.request;

        if (!string.IsNullOrWhiteSpace(request.title)) {
            novel.title = request.title;
        }

        if (!string.IsNullOrWhiteSpace(request.sourceLanguage)) {
            novel.sourceLanguage = request.sourceLanguage;
        }

        if (!string.IsNullOrWhiteSpace(request.targetLanguage)) {
            novel.targetLanguage = request.targetLanguage;
        }

        if (!string.IsNullOrWhiteSpace(request.model)) {
            novel.model = request.model;
        }

        if (request.styleGuide is not null) {
            novel.styleGuide = request.styleGuide.Length == 0 ? null : request.styleGuide;
        }

        if (request.normalizeQuotes is not null) {
            novel.normalizeQuotes = request.normalizeQuotes.Value;
        }

        await database.SaveChangesAsync(cancellationToken);

        return novel;
    }
}
