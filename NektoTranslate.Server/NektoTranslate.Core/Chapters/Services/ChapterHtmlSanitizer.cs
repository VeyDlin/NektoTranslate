using AngleSharp.Dom;
using AngleSharp.Html.Parser;


namespace NektoTranslate.Chapters.Services;


public interface IChapterHtmlSanitizer {

    string Sanitize(string html);
}


// Reduces whatever a parser scraped down to the handful of tags that carry authorial meaning.
// Site navigation, advertising, classes and inline styles are removed; what survives is emphasis,
// structure, illustrations and ruby. This is what keeps "we store HTML" from meaning "we store a
// web page".
public class ChapterHtmlSanitizer : IChapterHtmlSanitizer {

    private static readonly HashSet<string> allowedElements = [
        "p", "h1", "h2", "h3", "h4", "blockquote", "ul", "ol", "li", "hr", "pre",
        "br", "em", "strong", "i", "b", "a", "img", "ruby", "rt", "rp"
    ];

    private static readonly HashSet<string> blockElements = [
        "p", "h1", "h2", "h3", "h4", "blockquote", "ul", "ol", "li", "hr"
    ];

    // Removed with their contents. Everything else that is merely unknown gets unwrapped instead,
    // because an unknown wrapper usually still contains the prose.
    private static readonly HashSet<string> droppedElements = [
        "script", "style", "noscript", "iframe", "svg", "canvas", "template",
        "nav", "header", "footer", "aside", "form", "button", "input", "select", "textarea"
    ];

    private static readonly Dictionary<string, HashSet<string>> allowedAttributes = new() {
        ["a"] = ["href"],
        ["img"] = ["src", "alt"]
    };


    public string Sanitize(string html) {
        if (string.IsNullOrWhiteSpace(html)) {
            return string.Empty;
        }

        HtmlParser parser = new HtmlParser();
        IDocument document = parser.ParseDocument(html);
        IElement? body = document.Body;

        if (body is null) {
            return string.Empty;
        }

        foreach (IElement element in body.QuerySelectorAll("*").ToList()) {
            if (droppedElements.Contains(element.LocalName)) {
                element.Remove();
            }
        }

        foreach (IElement element in body.QuerySelectorAll("*").ToList()) {
            if (element.Parent is null) {
                continue;
            }

            if (allowedElements.Contains(element.LocalName)) {
                StripAttributes(element);
            } else {
                Unwrap(element);
            }
        }

        NormaliseToBlocks(document, body);

        return body.InnerHtml.Trim();
    }


    // Guarantees that every piece of text ends up inside a block element.
    //
    // Sites do not oblige with tidy paragraphs. Some wrap a whole chapter in one <pre>, some use
    // bare <div>s, some put the text straight in the body and separate paragraphs with double <br>.
    // Unwrapping those leaves text with no block around it, and a segmenter that looks for blocks
    // then finds nothing - which is not an empty chapter but a chapter whose text has silently
    // ceased to exist.
    private static void NormaliseToBlocks(IDocument document, IElement body) {
        // A <pre> carries its structure in line breaks, which collapse the moment the tag goes away.
        // Each line becomes a paragraph while the breaks are still there to read.
        foreach (IElement pre in body.QuerySelectorAll("pre").ToList()) {
            foreach (string line in pre.TextContent.ReplaceLineEndings("\n").Split('\n')) {
                if (line.Trim().Length == 0) {
                    continue;
                }

                IElement paragraph = document.CreateElement("p");
                paragraph.TextContent = line.Trim();
                pre.Parent?.InsertBefore(paragraph, pre);
            }

            pre.Remove();
        }

        List<INode> buffer = [];
        bool previousWasBreak = false;

        foreach (INode node in body.ChildNodes.ToList()) {
            if (node is IElement element && blockElements.Contains(element.LocalName)) {
                WrapBuffer(document, body, buffer, element);
                previousWasBreak = false;
                continue;
            }

            // Two breaks in a row are how a paragraph is written when nobody used <p>.
            if (node is IElement br && br.LocalName == "br") {
                if (previousWasBreak) {
                    br.Remove();
                    WrapBuffer(document, body, buffer, null);
                    continue;
                }

                previousWasBreak = true;
                buffer.Add(node);
                continue;
            }

            if (node.NodeType == NodeType.Text && node.TextContent.Trim().Length == 0) {
                continue;
            }

            previousWasBreak = false;
            buffer.Add(node);
        }

        WrapBuffer(document, body, buffer, null);
    }


    private static void WrapBuffer(IDocument document, IElement body, List<INode> buffer, INode? anchor) {
        if (buffer.Count == 0) {
            return;
        }

        if (buffer.All(node => node.TextContent.Trim().Length == 0 && node is not IElement { LocalName: "img" })) {
            buffer.Clear();

            return;
        }

        IElement paragraph = document.CreateElement("p");

        foreach (INode node in buffer) {
            paragraph.AppendChild(node);
        }

        if (anchor is null) {
            body.AppendChild(paragraph);
        } else {
            body.InsertBefore(paragraph, anchor);
        }

        buffer.Clear();
    }


    private static void StripAttributes(IElement element) {
        allowedAttributes.TryGetValue(element.LocalName, out HashSet<string>? allowed);

        foreach (IAttr attribute in element.Attributes.ToList()) {
            if (allowed is null || !allowed.Contains(attribute.Name)) {
                element.RemoveAttribute(attribute.Name);
            }
        }
    }


    private static void Unwrap(IElement element) {
        INode? parent = element.Parent;

        if (parent is null) {
            return;
        }

        while (element.FirstChild is not null) {
            parent.InsertBefore(element.FirstChild, element);
        }

        element.Remove();
    }
}
