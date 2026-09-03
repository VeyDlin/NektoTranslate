using Microsoft.Extensions.Logging;
using NektoTranslate.Translation.Contracts;


namespace NektoTranslate.Translation.Checks;


public interface ITranslationCheckRunner {

    IReadOnlyList<TranslationIssue> Run(TranslationCheckContext context);
}


// Runs every check that applies to the language pair at hand.
//
// Checks are collected through DI, so adding one is adding a class. A check that throws is reported
// and skipped rather than allowed to fail the chapter: a quality report is worth less than the
// translation it describes, and losing the chapter because a report could not be produced would be
// the wrong trade.
public class TranslationCheckRunner(
    IEnumerable<ITranslationCheck> checks,
    ILogger<TranslationCheckRunner> logger
) : ITranslationCheckRunner {

    public IReadOnlyList<TranslationIssue> Run(TranslationCheckContext context) {
        List<TranslationIssue> issues = [];

        foreach (ITranslationCheck check in checks) {
            if (!check.AppliesTo(context.sourceLanguage, context.targetLanguage)) {
                continue;
            }

            try {
                issues.AddRange(check.Run(context));
            } catch (Exception failure) {
                logger.LogWarning(failure, "Translation check {Check} failed", check.name);
            }
        }

        return issues;
    }
}
