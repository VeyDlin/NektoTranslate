using NektoTranslate.Glossary.Enums;


namespace NektoTranslate.Glossary.Services;


// Which source of a rendering outranks which.
//
// A rendering recovered from a translation the book already has takes precedence over anything the
// model invents: it is what the reader has already met, and a book that renames its characters
// halfway through is worse than one translated slightly less elegantly. The rule holds in both
// directions - an established rendering is never overwritten by an invented one, and an invented
// one is replaced the moment the existing translation turns out to have an answer.
//
// A hand edit outranks both. That is the user overriding on purpose, and silently reverting it on
// the next chapter would make the glossary feel broken.
public static class GlossaryPrecedence {

    public static int RankOf(GlossaryEntryOrigin origin) {
        return origin switch {
            GlossaryEntryOrigin.Manual => 3,
            GlossaryEntryOrigin.FromExistingTranslation => 2,
            GlossaryEntryOrigin.AiExtracted => 1,
            _ => 0
        };
    }


    public static bool Outranks(GlossaryEntryOrigin candidate, GlossaryEntryOrigin established) {
        return RankOf(candidate) > RankOf(established);
    }


    // Whether a stored entry still deserves to be checked against the existing translation. An
    // entry the model invented does; one already backed by the translation, or set by hand, does
    // not.
    public static bool IsOpenToRevision(GlossaryEntryOrigin origin) {
        return RankOf(origin) < RankOf(GlossaryEntryOrigin.FromExistingTranslation);
    }
}
