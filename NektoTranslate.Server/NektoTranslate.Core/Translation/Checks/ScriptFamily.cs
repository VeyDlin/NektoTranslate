namespace NektoTranslate.Translation.Checks;


public enum Script {
    Unknown = 0,
    Latin = 1,
    Cyrillic = 2,
    Ideographic = 3,
    Other = 4
}


// Works out which script a passage is written in, so a check can tell whether two languages are
// even distinguishable by their characters.
//
// Detection is by the text rather than by the language name, because language names arrive as free
// text - the user types "Japanese" or "日本語" or "ja" - and a check that depended on spelling them
// a particular way would silently stop applying.
public static class ScriptFamily {

    // A passage is assigned the script that most of its letters belong to. Punctuation, digits and
    // spaces are ignored: they are shared and would drag every sample towards Latin.
    public static Script Detect(string text) {
        int latin = 0;
        int cyrillic = 0;
        int ideographic = 0;
        int other = 0;

        foreach (char character in text) {
            if (!char.IsLetter(character)) {
                continue;
            }

            if (IsIdeographic(character)) {
                ideographic++;
            } else if (character is >= 'А' and <= 'ӿ') {
                cyrillic++;
            } else if (character < 'Ͱ') {
                latin++;
            } else {
                other++;
            }
        }

        int total = latin + cyrillic + ideographic + other;

        if (total == 0) {
            return Script.Unknown;
        }

        int best = Math.Max(Math.Max(latin, cyrillic), Math.Max(ideographic, other));

        if (best == ideographic) {
            return Script.Ideographic;
        }

        if (best == cyrillic) {
            return Script.Cyrillic;
        }

        return best == latin ? Script.Latin : Script.Other;
    }


    public static bool IsIdeographic(char character) {
        return character is (>= '぀' and <= 'ヿ')
            or (>= '㐀' and <= '䶿')
            or (>= '一' and <= '鿿')
            or (>= '豈' and <= '﫿')
            or (>= '가' and <= '힯');
    }
}
