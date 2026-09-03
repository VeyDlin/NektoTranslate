using NektoTranslate.Common.Models;
using NektoTranslate.Translation.Contracts;


namespace NektoTranslate.Translation.Checks;


// Reports blocks that came back exactly as they went in.
//
// A block identical to its source is a block the model copied instead of translating. It passes
// every structural check - the count is right and the Markdown is intact - and reads as a defect
// only to someone who knows both languages.
//
// Gated on the two languages actually differing in script. Between related languages written the
// same way, an identical line can be a legitimate translation: a proper name on its own, a number,
// an interjection.
public class UntranslatedBlockCheck(EngineOptions options) : ITranslationCheck {

    private readonly int minimumLength = options.checks.minimumBlockLength;

    private readonly int maxReported = options.checks.maxReported;


    public string name => "untranslated-block";


    public bool AppliesTo(string sourceLanguage, string targetLanguage) {
        return !string.Equals(sourceLanguage, targetLanguage, StringComparison.OrdinalIgnoreCase);
    }


    public IReadOnlyList<TranslationIssue> Run(TranslationCheckContext context) {
        if (ScriptFamily.Detect(context.sourcePlainText) == ScriptFamily.Detect(context.translatedPlainText)) {
            return [];
        }

        string[] source = context.sourcePlainText.ReplaceLineEndings("\n").Split('\n');
        string[] translated = context.translatedPlainText.ReplaceLineEndings("\n").Split('\n');
        List<TranslationIssue> issues = [];

        for (int index = 0; index < source.Length && index < translated.Length; index++) {
            string original = source[index].Trim();

            if (original.Length < minimumLength) {
                continue;
            }

            if (original == translated[index].Trim()) {
                issues.Add(new TranslationIssue(
                    name,
                    "This block came back unchanged from the source.",
                    index
                ));

                if (issues.Count == maxReported) {
                    break;
                }
            }
        }

        return issues;
    }
}
