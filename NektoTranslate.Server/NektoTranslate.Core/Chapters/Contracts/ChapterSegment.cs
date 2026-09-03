namespace NektoTranslate.Chapters.Contracts;


// One translatable block of a chapter: a paragraph, a heading, a quote, a list.
//
// `text` is Markdown. Inline formatting travels with the text rather than being lifted into
// placeholders, because Markdown's inline syntax is light enough that a model preserves it
// naturally - which is the simplification that storing Markdown buys.
public sealed record ChapterSegment(
    int index,
    string text
) {

    public string PlainText() {
        return MarkdownText.Strip(text);
    }
}
