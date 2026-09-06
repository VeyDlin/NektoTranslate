using Mediator;
using NektoTranslate.Jobs.Contracts;
using NektoTranslate.Jobs.Entities;
using NektoTranslate.Jobs.Services;


namespace NektoTranslate.Jobs.Commands;


public record StartTranslationJobCommand(
    long novelId,
    StartTranslationJobRequest request
) : ICommand<TranslationJob>;


public class StartTranslationJobCommandHandler(ITranslationJobService jobs)
    : ICommandHandler<StartTranslationJobCommand, TranslationJob> {

    public async ValueTask<TranslationJob> Handle(
        StartTranslationJobCommand command,
        CancellationToken cancellationToken
    ) {
        return await jobs.EnqueueAsync(
            command.novelId,
            command.request.mode,
            command.request.scopeKind,
            command.request.fromIndex,
            command.request.toIndex,
            command.request.chapterIds ?? [],
            command.request.budgetUsd,
            command.request.force ?? false,
            cancellationToken
        );
    }
}
