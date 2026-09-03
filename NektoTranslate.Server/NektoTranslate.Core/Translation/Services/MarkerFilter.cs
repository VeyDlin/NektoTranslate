using System.Text;
using NektoTranslate.Chapters.Contracts;


namespace NektoTranslate.Translation.Services;


// Strips protocol markers out of a stream of partial text so a reader watching a chapter being
// written sees prose rather than ⟦#3⟧ and ⟦1⟧.
//
// Stateful because deltas arrive in arbitrary chunks: a marker can be split across two of them, and
// a filter that looked at each chunk alone would let half a marker through. Anything after an
// unclosed opening bracket is therefore held back until the bracket closes or the stream ends.
public class MarkerFilter {

    private readonly StringBuilder pending = new StringBuilder();

    private bool insideMarker;


    public string Push(string chunk) {
        StringBuilder output = new StringBuilder(chunk.Length);

        foreach (char character in chunk) {
            if (insideMarker) {
                pending.Append(character);

                if (character == Placeholders.Close) {
                    insideMarker = false;
                    pending.Clear();
                } else if (pending.Length > MaxMarkerLength) {
                    // Not a marker after all - an opening bracket that occurs in the prose itself.
                    // Release what was held rather than swallowing text.
                    insideMarker = false;
                    output.Append(pending);
                    pending.Clear();
                }

                continue;
            }

            if (character == Placeholders.Open) {
                insideMarker = true;
                pending.Append(character);
                continue;
            }

            output.Append(character);
        }

        return output.ToString();
    }


    // Whatever is still held when the stream ends was never a marker.
    public string Flush() {
        string remainder = pending.ToString();

        pending.Clear();
        insideMarker = false;

        return remainder;
    }


    // A marker is a bracket, an optional '#' or '/', a few digits and a closing bracket. Anything
    // longer is prose.
    private const int MaxMarkerLength = 8;
}
