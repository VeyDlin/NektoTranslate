using Mediator;
using Microsoft.EntityFrameworkCore;
using NektoTranslate.Common.Data;
using NektoTranslate.Glossary.Contracts;
using NektoTranslate.Glossary.Entities;
using NektoTranslate.Glossary.Enums;


namespace NektoTranslate.Glossary.Commands;


public record UpsertGlossaryEntryCommand(
    long novelId,
    UpsertGlossaryEntryRequest request
) : ICommand<GlossaryEntry>;


// A hand edit. Always stored as Manual, which is the top of the precedence order, so the agent will
// not quietly revert it on the next chapter - reverting a correction the user made on purpose is
// what makes a glossary feel broken.
public class UpsertGlossaryEntryCommandHandler(NektoDbContext database)
    : ICommandHandler<UpsertGlossaryEntryCommand, GlossaryEntry> {

    public async ValueTask<GlossaryEntry> Handle(
        UpsertGlossaryEntryCommand command,
        CancellationToken cancellationToken
    ) {
        UpsertGlossaryEntryRequest request = command.request;

        GlossaryEntry? entry = await database.glossaryEntries.FirstOrDefaultAsync(
            candidate => candidate.novelId == command.novelId
                && candidate.language == request.language
                && candidate.sourceTerm == request.sourceTerm,
            cancellationToken
        );

        if (entry is null) {
            entry = new GlossaryEntry {
                novelId = command.novelId,
                language = request.language,
                sourceTerm = request.sourceTerm,
                targetTerm = request.targetTerm
            };

            database.glossaryEntries.Add(entry);
        }

        entry.targetTerm = request.targetTerm;
        entry.category = request.category ?? entry.category;
        entry.notes = request.notes;
        entry.aliases = request.aliases?.ToList() ?? entry.aliases;
        entry.origin = GlossaryEntryOrigin.Manual;
        entry.needsReview = false;
        entry.updatedAt = DateTimeOffset.UtcNow;

        await database.SaveChangesAsync(cancellationToken);

        return entry;
    }
}
