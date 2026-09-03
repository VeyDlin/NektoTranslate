using NektoTranslate.Translation.Contracts;


namespace NektoTranslate.Translation.Checks;


// One post-translation quality check.
//
// A named, extensible set rather than a single hard-coded check, because a translation can be wrong
// in several unrelated ways and each needs its own evidence. More importantly, several of them are
// only meaningful for particular language pairs - a check for leftover source characters means one
// thing translating Japanese to Russian and nothing at all translating English to German - so every
// check declares for itself whether it applies.
//
// That declaration is the point. Without it, language-specific rules get written as if one pair
// were the only pair, which is how a translator that accepts arbitrary languages ends up quietly
// assuming its author's favourite one.
public interface ITranslationCheck {

    string name { get; }


    bool AppliesTo(string sourceLanguage, string targetLanguage);


    IReadOnlyList<TranslationIssue> Run(TranslationCheckContext context);
}
