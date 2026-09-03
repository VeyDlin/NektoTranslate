namespace NektoTranslate.Glossary.Services;


// Decides whether a term occurs in a passage, in a way that holds for every language pair the
// application accepts rather than only for Japanese.
//
// The two families need opposite rules:
//
//   Ideographic scripts - Chinese, Japanese, Korean hanja - have no spaces and no letter case.
//   A plain substring test is correct, and imposing word boundaries would find nothing, because
//   there are no boundaries to find. Particles attach directly to the noun, so 田中 must still match
//   inside 田中さんは.
//
//   Alphabetic scripts - Latin, Cyrillic, Greek - need the opposite. A plain substring test finds
//   "Ann" inside "Announcement" and "Рим" inside "Кримсон", inventing occurrences that are not
//   there, and a case-sensitive one misses a name that opens a sentence. So the match is
//   case-insensitive and bounded by non-letters.
//
// Suffix inflection still works under the bounded rule, because the term is matched as a prefix of
// the word: "Иван" is found in "Ивана" and "Ивану". That is the common case in Russian, Polish and
// German. A language that inflects by changing the stem is beyond what exact matching can do, and
// is the reason ITermLocator is an interface with room for a stemming implementation.
public static class TermMatching {

    // How many letters may follow the term and still count as the same word.
    //
    // The right edge cannot simply be a boundary, or "Иван" would not be found in "Ивана", and it
    // cannot be open either, or "Ann" would be found in "announcement". A short tail is an
    // inflectional ending; a long one is a different word. Three covers the case endings of Russian,
    // Polish, Czech and German without swallowing compounds.
    //
    // Configurable because the right number is a property of the target language rather than of the
    // algorithm: Finnish or Hungarian case endings run longer than three letters. Defaulted here so
    // callers without configuration - the tests among them - still behave as before.
    public const int DefaultMaxInflectionLength = 3;


    public static bool Contains(
        string haystack,
        string term,
        int maxInflectionLength = DefaultMaxInflectionLength
    ) {
        if (string.IsNullOrEmpty(term) || string.IsNullOrEmpty(haystack)) {
            return false;
        }

        return IsIdeographic(term)
            ? haystack.Contains(term, StringComparison.Ordinal)
            : ContainsAsWord(haystack, term, maxInflectionLength);
    }


    // A term counts as ideographic when any of its characters is: a mixed string such as a name
    // followed by a Latin numeral still belongs to the script that has no word boundaries.
    public static bool IsIdeographic(string term) {
        return term.Any(IsIdeographic);
    }


    private static bool ContainsAsWord(string haystack, string term, int maxInflectionLength) {
        int from = 0;

        while (from <= haystack.Length - term.Length) {
            int at = haystack.IndexOf(term, from, StringComparison.OrdinalIgnoreCase);

            if (at < 0) {
                return false;
            }

            bool startsWord = at == 0 || !char.IsLetter(haystack[at - 1]);

            if (startsWord && TrailingLetters(haystack, at + term.Length) <= maxInflectionLength) {
                return true;
            }

            from = at + 1;
        }

        return false;
    }


    private static int TrailingLetters(string haystack, int from) {
        int count = 0;

        while (from + count < haystack.Length && char.IsLetter(haystack[from + count])) {
            count++;
        }

        return count;
    }


    private static bool IsIdeographic(char character) {
        return character is (>= '぀' and <= 'ヿ')
            or (>= '㐀' and <= '䶿')
            or (>= '一' and <= '鿿')
            or (>= '豈' and <= '﫿')
            or (>= '가' and <= '힯');
    }
}
