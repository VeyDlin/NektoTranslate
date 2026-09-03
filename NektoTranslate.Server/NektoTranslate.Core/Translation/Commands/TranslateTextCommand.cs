using Mediator;
using NektoTranslate.Translation.Contracts;
using NektoTranslate.Translation.Services;


namespace NektoTranslate.Translation.Commands;


public record TranslateTextCommand(TranslateTextRequest request) : ICommand<TranslationResult>;


public class TranslateTextCommandHandler(ITranslator translator)
    : ICommandHandler<TranslateTextCommand, TranslationResult> {

    public async ValueTask<TranslationResult> Handle(
        TranslateTextCommand command,
        CancellationToken cancellationToken
    ) {
        return await translator.TranslateAsync(command.request, cancellationToken);
    }
}
