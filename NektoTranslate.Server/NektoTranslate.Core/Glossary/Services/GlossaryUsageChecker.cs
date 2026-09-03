using NektoTranslate.Common.Models;
using NektoTranslate.Translation.Contracts;


namespace NektoTranslate.Glossary.Services;


public interface IGlossaryUsageChecker {

    IReadOnlyList<GlossaryTerm> FindIgnored(
        IReadOnlyList<GlossaryTerm> supplied,
        string sourcePlainText,
        string translatedPlainText
    );
}


// Checks whether the renderings handed to the model actually came back in its output.
//
// Supplying a glossary is a request, not a guarantee: the model can and does paraphrase a name it
// was told to keep. For a rendering that came from the book's own translation that is a real
// defect, because the reader already knows the character by that name - so the run reports it
// instead of assuming compliance.
//
// A term counts as ignored only when its source form is present in the chapter and none of its
// accepted renderings appear in the translation. Alternatives are separated by '/', which is how a
// name with more than one legitimate form is written.
public class GlossaryUsageChecker(EngineOptions options) : IGlossaryUsageChecker {

    private readonly int maxInflectionLength = options.glossary.maxInflectionLength;


    public IReadOnlyList<GlossaryTerm> FindIgnored(
        IReadOnlyList<GlossaryTerm> supplied,
        string sourcePlainText,
        string translatedPlainText
    ) {
        List<GlossaryTerm> ignored = [];

        foreach (GlossaryTerm term in supplied) {
            if (!TermMatching.Contains(sourcePlainText, term.sourceTerm, maxInflectionLength)) {
                continue;
            }

            // The target side is matched loosely on purpose: a rendering that came back inflected
            // ("Танаку" for "Танака") has been used, and reporting it as ignored would be a false
            // alarm on every language that declines names.
            bool used = term.targetTerm
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(rendering => translatedPlainText.Contains(rendering, StringComparison.OrdinalIgnoreCase));

            if (!used) {
                ignored.Add(term);
            }
        }

        return ignored;
    }
}
