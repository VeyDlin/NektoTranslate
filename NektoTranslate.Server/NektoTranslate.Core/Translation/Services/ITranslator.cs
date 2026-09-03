using NektoTranslate.Translation.Contracts;


namespace NektoTranslate.Translation.Services;


public interface ITranslator {
    Task<TranslationResult> TranslateAsync(TranslateTextRequest request, CancellationToken cancellationToken = default);
}
