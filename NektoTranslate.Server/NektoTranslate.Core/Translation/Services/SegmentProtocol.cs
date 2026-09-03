using System.Text;
using NektoTranslate.Chapters.Contracts;


namespace NektoTranslate.Translation.Services;


// The wire format between us and the model: one marked line per segment.
//
//   ⟦#0⟧первый абзац
//   ⟦#1⟧второй абзац
//
// The marker carries the segment's own index rather than relying on line order, so a reply that
// merged, dropped or reordered segments is detected on arrival instead of quietly producing a
// chapter whose paragraphs have shifted by one. The brackets are the same pair used for inline
// placeholders, disambiguated by the leading '#'.
public static class SegmentProtocol {

    public static string Format(IReadOnlyList<string> segments) {
        StringBuilder result = new StringBuilder();

        for (int index = 0; index < segments.Count; index++) {
            result.Append(Marker(index));
            result.AppendLine(segments[index].ReplaceLineEndings(" "));
        }

        return result.ToString().TrimEnd();
    }


    // Segment bodies may span several lines: the model sometimes wraps long prose, and refusing
    // the chapter over a line break would be a needless retry. Anything before the first marker is
    // discarded, which absorbs a stray preamble without losing content.
    public static IReadOnlyList<string> Parse(string response, int expectedCount) {
        Dictionary<int, StringBuilder> bodies = [];
        int? current = null;

        foreach (string line in response.ReplaceLineEndings("\n").Split('\n')) {
            int? marked = ReadMarker(line, out string remainder);

            if (marked is not null) {
                current = marked;
                bodies[current.Value] = new StringBuilder(remainder);
                continue;
            }

            if (current is not null && line.Trim().Length > 0) {
                bodies[current.Value].Append(' ').Append(line.Trim());
            }
        }

        List<string> segments = [];

        for (int index = 0; index < expectedCount; index++) {
            if (!bodies.TryGetValue(index, out StringBuilder? body)) {
                // The reply itself is the only useful evidence about why it did not parse, so it
                // travels with the error rather than being discarded into a retry.
                throw new SegmentProtocolException(
                    $"Segment {index} is missing from the reply; {bodies.Count} of {expectedCount} arrived. "
                    + $"Reply began: {Excerpt(response)}"
                );
            }

            segments.Add(body.ToString().Trim());
        }

        return segments;
    }


    private const int ExcerptLength = 300;


    private static string Excerpt(string response) {
        string trimmed = response.Trim().ReplaceLineEndings(" ");

        if (trimmed.Length == 0) {
            return "<empty>";
        }

        return trimmed.Length <= ExcerptLength ? trimmed : trimmed[..ExcerptLength] + "...";
    }


    private static string Marker(int index) {
        return $"{Placeholders.Open}#{index}{Placeholders.Close}";
    }


    private static int? ReadMarker(string line, out string remainder) {
        remainder = line;

        string trimmed = line.TrimStart();
        int close = trimmed.IndexOf(Placeholders.Close);

        if (trimmed.Length < 4 || trimmed[0] != Placeholders.Open || close < 2 || trimmed[1] != '#') {
            return null;
        }

        if (!int.TryParse(trimmed[2..close], out int index)) {
            return null;
        }

        remainder = trimmed[(close + 1)..].Trim();

        return index;
    }
}
