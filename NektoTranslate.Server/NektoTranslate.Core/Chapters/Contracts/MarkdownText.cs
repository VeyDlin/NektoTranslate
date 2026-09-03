using System.Text;
using System.Text.RegularExpressions;


namespace NektoTranslate.Chapters.Contracts;


// Reduces a Markdown block to the words in it.
//
// Exists for one reason: term search must not fail because of syntax. A name wrapped in emphasis,
// or sitting inside a ruby annotation, is still that name - and an index built over the raw
// Markdown would not find it, which is the same class of bug as searching raw HTML.
public static partial class MarkdownText {

    public static string Strip(string markdown) {
        string text = RubyAnnotation().Replace(markdown, "$1");
        text = InlineHtml().Replace(text, string.Empty);
        text = Image().Replace(text, "$1");
        text = Link().Replace(text, "$1");
        text = Emphasis().Replace(text, "$1");
        text = BlockPrefix().Replace(text, string.Empty);

        return text.Trim();
    }


    public static string StripRuby(string markdown) {
        return RubyAnnotation().Replace(markdown, "$1");
    }


    // Everything a chapter is split on. A blank line separates blocks in Markdown, and a run of
    // several is still one separation.
    public static List<string> SplitBlocks(string markdown) {
        List<string> blocks = [];
        StringBuilder current = new StringBuilder();

        foreach (string line in markdown.ReplaceLineEndings("\n").Split('\n')) {
            if (line.Trim().Length == 0) {
                Flush(blocks, current);
                continue;
            }

            if (current.Length > 0) {
                current.Append('\n');
            }

            current.Append(line);
        }

        Flush(blocks, current);

        return blocks;
    }


    private static void Flush(List<string> blocks, StringBuilder current) {
        if (current.Length == 0) {
            return;
        }

        string block = current.ToString().Trim();

        if (block.Length > 0) {
            blocks.Add(block);
        }

        current.Clear();
    }


    // Keeps the base characters and drops the reading: <ruby>兄<rt>あに</rt></ruby> -> 兄
    [GeneratedRegex(@"<ruby>(.*?)<rt>.*?</rt>\s*</ruby>", RegexOptions.Singleline)]
    private static partial Regex RubyAnnotation();


    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex InlineHtml();


    [GeneratedRegex(@"!\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex Image();


    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex Link();


    [GeneratedRegex(@"\*{1,3}([^*]+)\*{1,3}")]
    private static partial Regex Emphasis();


    [GeneratedRegex(@"^\s{0,3}(#{1,6}\s+|>\s?|[-*+]\s+|\d+\.\s+)", RegexOptions.Multiline)]
    private static partial Regex BlockPrefix();
}
