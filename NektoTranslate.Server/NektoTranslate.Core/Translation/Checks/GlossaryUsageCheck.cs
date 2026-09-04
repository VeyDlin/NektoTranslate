using NektoTranslate.Common.Contracts;
using NektoTranslate.Glossary.Services;
using NektoTranslate.Translation.Contracts;


namespace NektoTranslate.Translation.Checks;


// Reports established renderings the model was given and did not use.
//
// Supplying a glossary is a request, not a guarantee: the model can and does paraphrase a name it
// was told to keep. For a rendering that came from the book's own earlier translation that is a real
// defect, because the reader already knows the character by that name.
//
// Applies to every language pair - term consistency is not a property of any one script.
public class GlossaryUsageCheck(IGlossaryUsageChecker checker) : ITranslationCheck {

    public string name => "glossary-ignored";


    public bool AppliesTo(string sourceLanguage, string targetLanguage) {
        return true;
    }


    public IReadOnlyList<TranslationIssue> Run(TranslationCheckContext context) {
        return checker
            .FindIgnored(context.suppliedTerms, context.sourcePlainText, context.translatedPlainText)
            .Select(term => new TranslationIssue(
                name,
                Statuses.GlossaryTermIgnored.With(("source", term.sourceTerm), ("target", term.targetTerm))
            ))
            .ToList();
    }
}
