using NektoTranslate.Common.Contracts;
using NektoTranslate.Common.Models;
using NektoTranslate.Translation.Contracts;


namespace NektoTranslate.Translation.Checks;


// Reports source-script characters that survived into the translation.
//
// A run of Japanese left inside Russian prose is almost always a fragment the model skipped rather
// than a deliberate choice, and it is invisible to every structural check: the block count is right,
// the Markdown is valid, and the chapter reads correctly until the reader hits a line of kanji.
//
// Only applies when the two languages use different scripts. Translating English to German, Latin
// characters in the output are the output, and the check would fire on every chapter. This is
// exactly the kind of rule that has to be gated rather than assumed.
public class UntranslatedResidueCheck(EngineOptions options) : ITranslationCheck {

    // A few stray characters are ordinary - a name kept in its original spelling, a piece of
    // punctuation. A run of them is a skipped sentence.
    private readonly int minimumRun = options.checks.minimumResidueRun;

    private readonly int maxReported = options.checks.maxReported;


    public string name => "untranslated-residue";


    public bool AppliesTo(string sourceLanguage, string targetLanguage) {
        // Decided per chapter from the text itself, since the language names are free text. Nothing
        // can be ruled out from the names alone.
        return true;
    }


    public IReadOnlyList<TranslationIssue> Run(TranslationCheckContext context) {
        // Which script counts as residue is read off the original's own script. Without an original
        // the question cannot be asked from the text alone — the language names are free text and
        // cannot be turned into a script family reliably enough to accuse a paragraph on.
        if (context.sourcePlainText is null) {
            return [];
        }

        Script source = ScriptFamily.Detect(context.sourcePlainText);
        Script target = ScriptFamily.Detect(context.translatedPlainText);

        if (source == Script.Unknown || source == target) {
            return [];
        }

        List<TranslationIssue> issues = [];

        // Scanned block by block rather than over the whole chapter at once, so each finding can say
        // where it is. The text is identical either way; what changes is that the reader can be
        // taken to the paragraph instead of being told to go and look for it.
        string[] blocks = context.translatedPlainText.ReplaceLineEndings("\n").Split('\n');

        for (int index = 0; index < blocks.Length && issues.Count < maxReported; index++) {
            foreach (string run in FindRuns(blocks[index], source, minimumRun)) {
                issues.Add(new TranslationIssue(
                    name,
                    Statuses.SourceScriptResidue.With(("run", run)),
                    index
                ));

                if (issues.Count == maxReported) {
                    break;
                }
            }
        }

        return issues;
    }


    private static IEnumerable<string> FindRuns(string text, Script sourceScript, int minimumRun) {
        List<string> runs = [];
        int start = -1;

        for (int index = 0; index <= text.Length; index++) {
            bool matches = index < text.Length
                && char.IsLetter(text[index])
                && ScriptOf(text[index]) == sourceScript;

            if (matches) {
                if (start < 0) {
                    start = index;
                }

                continue;
            }

            if (start >= 0) {
                if (index - start >= minimumRun) {
                    runs.Add(text[start..index]);
                }

                start = -1;
            }
        }

        return runs;
    }


    private static Script ScriptOf(char character) {
        return ScriptFamily.Detect(character.ToString());
    }
}
