using System.Text;


namespace NektoTranslate.Chapters.Contracts;


// Placeholder syntax for inline markup inside a segment: an opening marker, a closing marker and
// a self-closing form for void elements such as <br> and <img>.
//
//   ⟦1⟧emphasised⟦/1⟧    ⟦2/⟧
//
// The brackets are U+27E6 and U+27E7, which do not occur in prose in any language we translate,
// so a marker can never collide with the text around it.
public static class Placeholders {
    public const char Open = '\u27E6';
    public const char Close = '\u27E7';


    public static string OpenTag(int id) {
        return $"{Open}{id}{Close}";
    }


    public static string CloseTag(int id) {
        return $"{Open}/{id}{Close}";
    }


    public static string VoidTag(int id) {
        return $"{Open}{id}/{Close}";
    }


    // Removes every marker and keeps the text between them.
    public static string Strip(string text) {
        StringBuilder result = new StringBuilder(text.Length);
        int position = 0;

        while (position < text.Length) {
            char character = text[position];

            if (character == Open) {
                int end = text.IndexOf(Close, position + 1);

                if (end >= 0 && IsMarkerBody(text.AsSpan(position + 1, end - position - 1))) {
                    position = end + 1;
                    continue;
                }
            }

            result.Append(character);
            position++;
        }

        return result.ToString();
    }


    private static bool IsMarkerBody(ReadOnlySpan<char> body) {
        if (body.Length == 0) {
            return false;
        }

        if (body[0] == '/') {
            body = body[1..];
        } else if (body[^1] == '/') {
            body = body[..^1];
        }

        return body.Length > 0 && !body.ContainsAnyExcept("0123456789");
    }
}
