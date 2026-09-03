using System.Text;
using NektoTranslate.Translation.Contracts;


namespace NektoTranslate.Translation.Services;


// Decides how a chapter is cut up before it is sent to the model, and how the pieces are put back
// together afterwards.
//
// Some authors write chapters the length of a short book, so no step of the pipeline may assume a
// chapter fits in one request. The cascade is paragraph, then sentence, then a blind character
// window: the last step is what guarantees nothing oversized ever leaves, whatever the author's
// punctuation habits.
//
// Pure and separate from the translator on purpose - the cutting rules are where the interesting
// mistakes live, and they should be testable without spending money on a model.
// The budget it cuts to is passed in rather than fixed here: the output ceiling and the expansion
// factor are settings, because the right expansion factor genuinely differs by language pair and a
// wrong one either truncates replies or wastes half of every batch.
public static class SegmentChunker {

    // Splits any paragraph too large for one request, recording which paragraph each piece came
    // from so the translation can be reassembled with the chapter's paragraph count intact.
    public static (List<string> pieces, List<int> owners) Flatten(
        IReadOnlyList<string> segments,
        ChunkBudget budget
    ) {
        List<string> pieces = [];
        List<int> owners = [];

        for (int index = 0; index < segments.Count; index++) {
            foreach (string piece in SplitOversized(segments[index], budget)) {
                pieces.Add(piece);
                owners.Add(index);
            }
        }

        return (pieces, owners);
    }


    public static List<string> Rejoin(IReadOnlyList<string> translated, IReadOnlyList<int> owners, int segmentCount) {
        List<StringBuilder> joined = [];

        for (int index = 0; index < segmentCount; index++) {
            joined.Add(new StringBuilder());
        }

        for (int position = 0; position < translated.Count && position < owners.Count; position++) {
            StringBuilder target = joined[owners[position]];

            if (target.Length > 0) {
                target.Append(' ');
            }

            target.Append(translated[position]);
        }

        return joined.Select(builder => builder.ToString()).ToList();
    }


    public static List<IReadOnlyList<string>> Partition(IReadOnlyList<string> pieces, ChunkBudget budget) {
        List<IReadOnlyList<string>> batches = [];
        List<string> current = [];
        int size = 0;

        foreach (string piece in pieces) {
            if (current.Count > 0 && size + piece.Length > budget.maxCharacters) {
                batches.Add(current);
                current = [];
                size = 0;
            }

            current.Add(piece);
            size += piece.Length;
        }

        if (current.Count > 0) {
            batches.Add(current);
        }

        return batches;
    }


    // Characters alone mislead across scripts, and they mislead differently for each one. Three
    // tiers rather than two, because the application translates between arbitrary languages and a
    // single non-Latin bucket would misjudge most of them:
    //
    //   ideographic  ~1 character per token   - Chinese, Japanese, Korean
    //   other        ~2 characters per token  - Cyrillic, Greek, Arabic, Hebrew, Devanagari
    //   Latin        ~4 characters per token  - English and the languages written with it
    //
    // The middle tier is the one that was missing. Lumping Cyrillic in with Latin under-counted a
    // Russian source by roughly half, which would have sent batches twice the intended size - and
    // Russian is a source language here, not only a target.
    public static int EstimateTokens(string text) {
        int estimate = 0;

        foreach (char character in text) {
            if (IsDenseScript(character)) {
                estimate += 4;
            } else if (IsLatin(character)) {
                estimate += 1;
            } else {
                estimate += 2;
            }
        }

        return estimate / 4;
    }


    private static bool IsLatin(char character) {
        return character < 'Ͱ';
    }


    public static bool Fits(string text, ChunkBudget budget) {
        return text.Length <= budget.maxCharacters && EstimateTokens(text) <= budget.maxTokens;
    }


    private static List<string> SplitOversized(string segment, ChunkBudget budget) {
        if (Fits(segment, budget)) {
            return [segment];
        }

        List<string> sentences = SplitSentences(segment);

        if (sentences.Count <= 1) {
            return HardSplit(segment, budget);
        }

        List<string> pieces = [];
        StringBuilder current = new StringBuilder();

        // Sentences are accumulated up to the limit rather than emitted one per request: a chapter
        // of short lines would otherwise become hundreds of tiny calls, each paying for the system
        // prompt again.
        foreach (string sentence in sentences) {
            if (current.Length > 0 && current.Length + sentence.Length > budget.maxCharacters) {
                pieces.Add(current.ToString());
                current.Clear();
            }

            if (current.Length == 0 && !Fits(sentence, budget)) {
                pieces.AddRange(HardSplit(sentence, budget));
                continue;
            }

            current.Append(sentence);
        }

        if (current.Length > 0) {
            pieces.Add(current.ToString());
        }

        return pieces;
    }


    private static List<string> SplitSentences(string text) {
        List<string> sentences = [];
        StringBuilder current = new StringBuilder();

        foreach (char character in text) {
            current.Append(character);

            if (character is '。' or '！' or '？' or '.' or '!' or '?') {
                sentences.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0) {
            sentences.Add(current.ToString());
        }

        return sentences;
    }


    private static List<string> HardSplit(string text, ChunkBudget budget) {
        List<string> pieces = [];

        for (int start = 0; start < text.Length; start += budget.maxCharacters) {
            pieces.Add(text.Substring(start, Math.Min(budget.maxCharacters, text.Length - start)));
        }

        return pieces;
    }


    private static bool IsDenseScript(char character) {
        return character is (>= '　' and <= '鿿')
            or (>= '豈' and <= '﫿')
            or (>= '＀' and <= '￯');
    }
}
