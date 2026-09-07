using System.Text;
using NektoTranslate.Chapters.Contracts;
using NektoTranslate.Translation.Contracts;


namespace NektoTranslate.Translation.Services;


// The shape a careful editor works in, common to a translate run and a repair run: read the whole
// chapter once and write a memo before touching a single paragraph, read each paragraph with the
// couple of neighbours around it as context, and read the finished chapter once more afterwards to
// catch what only shows up once the whole thing is back together. Translate and repair differ only
// in what is being read and what may be changed - the shape, and everything in this file, is theirs
// to share.
//
// Pure and static on purpose, the same reason RepairPrompt and VoicePrompt are: what the model is
// told and what its reply is trusted to mean is where a subtle mistake actually lives, and that is
// worth asserting on directly rather than only through a live run.
public static class CarefulPass {

    private const string NoneMarker = "NONE";

    private const string RegisterHeading = "REGISTER";

    private const string PresentHeading = "PRESENT";

    private const string MisspelledHeading = "MISSPELLED";

    // How many paragraphs of the book's own good translation the repair prompt shows as an example,
    // and how long one has to be to be worth showing - a one-word paragraph proves nothing about
    // voice.
    public const int ExampleParagraphCount = 3;

    public const int MinimumExampleLength = 40;


    // ---- batching with context --------------------------------------------------------------

    public const string ContextHeading =
        "CONTEXT (for reading only - do not translate, repair or output these lines)";


    // The whole query text for one careful-pass batch: the body kept exactly as the segment
    // protocol already renders it, with its neighbouring paragraphs framing it on either side as
    // context. A side with nothing to show - the first batch has no contextBefore, the last has no
    // contextAfter - contributes no heading at all, so a chapter short enough for one batch reads
    // exactly as it always did.
    public static string BuildBatchPrompt(
        IReadOnlyList<string> contextBefore,
        IReadOnlyList<string> body,
        IReadOnlyList<string> contextAfter
    ) {
        StringBuilder prompt = new StringBuilder();

        if (contextBefore.Count > 0) {
            AppendContextBlock(prompt, contextBefore);
            prompt.AppendLine();
        }

        prompt.Append(SegmentProtocol.Format(body));

        if (contextAfter.Count > 0) {
            prompt.AppendLine();
            prompt.AppendLine();
            AppendContextBlock(prompt, contextAfter);
        }

        return prompt.ToString();
    }


    private static void AppendContextBlock(StringBuilder prompt, IReadOnlyList<string> lines) {
        prompt.AppendLine(ContextHeading);

        foreach (string line in lines) {
            prompt.Append("(context) ").AppendLine(line.ReplaceLineEndings(" "));
        }
    }


    // ---- the memo (step 1) -------------------------------------------------------------------

    // What one memo call concluded about a chapter before any paragraph of it is touched: the
    // register and cast a pass should already know, which of the book's established names and
    // terms this chapter actually uses, and - for a repair, where the question doubles as the
    // "who is who" check the old per-batch alignment pass used to make - which of them this
    // chapter's own text spells differently. misspelled is always empty for a translate memo: there
    // is no existing rendering yet for a name to be misspelled in.
    public sealed record Memo(
        string register,
        IReadOnlyList<string> present,
        IReadOnlyList<NameAlignment> misspelled
    ) {
        public static readonly Memo Empty = new Memo(string.Empty, [], []);
    }


    public static string BuildTranslateMemoPrompt(
        string sourceLanguage,
        string targetLanguage,
        IReadOnlyList<GlossaryTerm> glossary
    ) {
        StringBuilder prompt = new StringBuilder();

        prompt.AppendLine(
            $"You are reading one chapter of a {sourceLanguage} light novel before it is translated "
            + $"into {targetLanguage}, the way an editor reads a manuscript before marking it up."
        );
        prompt.AppendLine("Answer in exactly the sections below, in this order, and nothing else.");

        prompt.AppendLine();
        prompt.AppendLine(RegisterHeading);
        prompt.AppendLine(
            "In English, one or two sentences: who appears in this chapter, and its register and "
            + "tone."
        );

        prompt.AppendLine();
        prompt.AppendLine(PresentHeading);
        prompt.AppendLine(
            "One line per established term below that actually appears in this chapter, exactly as "
            + $"listed. Write {NoneMarker} if none of them appear."
        );

        if (glossary.Count > 0) {
            prompt.AppendLine();
            prompt.AppendLine("ESTABLISHED TERMS");

            foreach (GlossaryTerm term in glossary) {
                prompt.Append("- ").AppendLine(term.sourceTerm);
            }
        }

        return prompt.ToString();
    }


    // Replaces ChapterRepairer's old per-batch name-alignment pass outright: this asks the same
    // "which of these names does this chapter's own text spell differently" question, once for the
    // whole chapter instead of once per request batch, which is also what lets it catch a name
    // spelled two different ways in two different batches of the same chapter.
    public static string BuildRepairMemoPrompt(string language, IReadOnlyList<RepairTerm> names) {
        StringBuilder prompt = new StringBuilder();

        prompt.AppendLine(
            $"You are reading one existing {language} translation of a light novel chapter before it "
            + "is repaired, the way an editor reads a manuscript before marking it up. There is no "
            + "reliable original to check it against."
        );
        prompt.AppendLine("Answer in exactly the sections below, in this order, and nothing else.");

        prompt.AppendLine();
        prompt.AppendLine(RegisterHeading);
        prompt.AppendLine(
            "In English, one or two sentences: who appears in this chapter, and its register and "
            + "tone."
        );

        prompt.AppendLine();
        prompt.AppendLine(PresentHeading);
        prompt.AppendLine(
            "One line per established name below that actually appears in this chapter, exactly as "
            + $"listed. Write {NoneMarker} if none of them appear."
        );

        prompt.AppendLine();
        prompt.AppendLine(MisspelledHeading);
        prompt.AppendLine(
            "One line per established name this chapter's own text spells differently, exactly in "
            + $"this form: written in the chapter | established rendering. Write {NoneMarker} if "
            + "every name is already spelled correctly."
        );

        if (names.Count > 0) {
            prompt.AppendLine();
            prompt.AppendLine("ESTABLISHED NAMES");

            foreach (RepairTerm name in names) {
                prompt.Append("- ").AppendLine(name.term);
            }
        }

        return prompt.ToString();
    }


    public static Memo ParseTranslateMemo(string reply) {
        string normalized = reply.ReplaceLineEndings("\n").Trim();

        if (normalized.Length == 0) {
            return Memo.Empty;
        }

        if (!Has(normalized, RegisterHeading) && !Has(normalized, PresentHeading)) {
            // Nothing recognisable came back, but the reply is still kept as the memo's free text
            // rather than thrown away as though nothing had been noted.
            return new Memo(normalized, [], []);
        }

        string register = Section(normalized, RegisterHeading, PresentHeading).Trim();
        string present = Section(normalized, PresentHeading, null).Trim();

        return new Memo(register.Length > 0 ? register : normalized, ParseListLines(present), []);
    }


    // The MISSPELLED section is handed to RepairPrompt.ParseNameAlignments unchanged - the same
    // parser the old per-batch alignment pass already trusted to decide which of a reply's pairs
    // are safe to feed into a rewrite.
    public static Memo ParseRepairMemo(
        string reply,
        IReadOnlyList<RepairTerm> names,
        string chapterPlainText,
        int maxInflectionLength
    ) {
        string normalized = reply.ReplaceLineEndings("\n").Trim();

        if (normalized.Length == 0) {
            return Memo.Empty;
        }

        if (!Has(normalized, RegisterHeading) && !Has(normalized, PresentHeading) && !Has(normalized, MisspelledHeading)) {
            return new Memo(normalized, [], []);
        }

        string register = Section(normalized, RegisterHeading, PresentHeading).Trim();
        string present = Section(normalized, PresentHeading, MisspelledHeading).Trim();
        string misspelledSection = Section(normalized, MisspelledHeading, null).Trim();

        IReadOnlyList<NameAlignment> misspelled = RepairPrompt.ParseNameAlignments(
            misspelledSection,
            names,
            chapterPlainText,
            maxInflectionLength
        );

        return new Memo(register.Length > 0 ? register : normalized, ParseListLines(present), misspelled);
    }


    // Folded into every pass prompt of the chapter under THIS CHAPTER - free text first, then the
    // established names and terms the memo found present, so a batch that never mentions one of
    // them by itself still knows the chapter as a whole does.
    public static void AppendThisChapter(StringBuilder prompt, Memo memo) {
        prompt.AppendLine();
        prompt.AppendLine("THIS CHAPTER");

        if (memo.register.Length > 0) {
            prompt.AppendLine(memo.register);
        }

        if (memo.present.Count > 0) {
            prompt.Append("Established names and terms already in this chapter: ");
            prompt.AppendLine(string.Join(", ", memo.present));
        }
    }


    // ---- how to read each paragraph (step 2) -------------------------------------------------

    public static void AppendHowToReadForTranslate(StringBuilder prompt, string sourceLanguage, string targetLanguage) {
        prompt.AppendLine();
        prompt.AppendLine("HOW TO READ EACH PARAGRAPH");
        prompt.AppendLine(
            $"Read each paragraph as an editor would: translate it, then ask - does it read as "
            + $"native {targetLanguage} prose or as translated {sourceLanguage}; are its names and "
            + "terms exactly as established; is the meaning kept exactly, with nothing added or "
            + "dropped. Decide to leave your first rendering, touch it, or rewrite it outright - "
            + "most paragraphs need a touch."
        );
        prompt.AppendLine(
            $"Translate the meaning, not the sentence: a {sourceLanguage} construction that has no "
            + $"natural {targetLanguage} shape is rebuilt, not mirrored."
        );
    }


    public static void AppendHowToReadForRepair(StringBuilder prompt, string language) {
        prompt.AppendLine();
        prompt.AppendLine("HOW TO READ EACH PARAGRAPH");
        prompt.AppendLine(
            $"Read each paragraph as an editor would: does it read as native {language} prose, or "
            + "does it still read as translated, literal, or machine-rendered; are its names and "
            + "terms exactly as established; is the meaning kept exactly, with nothing invented or "
            + "lost. Decide to leave it, touch it, or rewrite it - most paragraphs need a touch."
        );
        prompt.AppendLine(
            "A paragraph that already reads as native prose in the established voice is returned "
            + "unchanged - a change is a decision, not a reflex."
        );
    }


    // Which of a chapter's own paragraphs are worth showing as an example of the book's good
    // translation - the first few long enough to actually demonstrate a voice rather than prove
    // nothing.
    public static IReadOnlyList<string> SelectExampleParagraphs(IEnumerable<string> paragraphs) {
        return paragraphs
            .Where(paragraph => paragraph.Trim().Length >= MinimumExampleLength)
            .Take(ExampleParagraphCount)
            .ToList();
    }


    public static void AppendExamples(StringBuilder prompt, IReadOnlyList<string> paragraphs) {
        if (paragraphs.Count == 0) {
            return;
        }

        prompt.AppendLine();
        prompt.AppendLine(
            "EXAMPLES - paragraphs from this book's own translation, already good. Match this voice "
            + "exactly."
        );

        foreach (string paragraph in paragraphs) {
            prompt.AppendLine(paragraph);
            prompt.AppendLine();
        }
    }


    // ---- the proofread (step 3) ---------------------------------------------------------------

    // One paragraph the proofreader flagged after the whole chapter was read back: which segment,
    // and what it found wrong there. The critique that goes back into the re-pass under WHAT THE
    // PROOFREADER FOUND, not a rewrite of its own - the proofreader states the problem, the pass
    // that already knows the glossary and the voice fixes it.
    public sealed record ProofreadFinding(int segmentIndex, string problem);


    public static string BuildTranslateProofreadSystemPrompt(
        string sourceLanguage,
        string targetLanguage,
        int segmentCount,
        IReadOnlyList<GlossaryTerm> glossary,
        string? voiceSummary
    ) {
        StringBuilder prompt = new StringBuilder();

        prompt.AppendLine(
            $"You are proofreading a finished {targetLanguage} translation of a {sourceLanguage} "
            + "light novel chapter against its source, the way an editor reads a chapter once more "
            + "after it is typeset."
        );
        prompt.AppendLine(
            $"The chapter is {segmentCount} numbered paragraphs, each shown as its source and its "
            + $"translation, both marked {SegmentProtocol.Marker(0)}."
        );
        AppendProofreadInstructions(
            prompt,
            "a calque or a sentence that still reads as translated rather than native prose, a name "
            + "or term against the glossary below, a tense or register slip, or a meaning that "
            + "drifted from the source"
        );

        AppendGlossary(prompt, glossary);
        AppendVoice(prompt, voiceSummary);

        return prompt.ToString();
    }


    public static string BuildRepairProofreadSystemPrompt(
        string language,
        int segmentCount,
        IReadOnlyList<RepairTerm> terms,
        string? voiceSummary
    ) {
        StringBuilder prompt = new StringBuilder();

        prompt.AppendLine(
            $"You are proofreading a finished, already-repaired {language} translation of a light "
            + "novel chapter, the way an editor reads a chapter once more after it is typeset. There "
            + "is no reliable original to check it against."
        );
        prompt.AppendLine(
            $"The chapter is {segmentCount} numbered paragraphs, marked {SegmentProtocol.Marker(0)}."
        );
        AppendProofreadInstructions(
            prompt,
            "a calque or a sentence that still reads as translated, literal, or machine-rendered, a "
            + "name or term against the glossary below, or a tense or register slip"
        );

        if (terms.Count > 0) {
            prompt.AppendLine();
            prompt.AppendLine("GLOSSARY - these renderings are already established.");

            foreach (RepairTerm term in terms) {
                prompt.Append("- ").AppendLine(term.term);
            }
        }

        AppendVoice(prompt, voiceSummary);

        return prompt.ToString();
    }


    private static void AppendProofreadInstructions(StringBuilder prompt, string whatToLookFor) {
        prompt.AppendLine(
            "For each paragraph that still needs work, answer one line exactly in this form: "
            + $"{SegmentProtocol.Marker(0)} | problem - " + whatToLookFor + ". State the problem in "
            + "a few words; do not rewrite the paragraph yourself."
        );
        prompt.AppendLine(
            $"A paragraph that needs nothing is not listed. If nothing needs work, answer {NoneMarker}."
        );
    }


    private static void AppendGlossary(StringBuilder prompt, IReadOnlyList<GlossaryTerm> glossary) {
        if (glossary.Count == 0) {
            return;
        }

        prompt.AppendLine();
        prompt.AppendLine("GLOSSARY - these renderings are already established.");

        foreach (GlossaryTerm term in glossary) {
            prompt.Append("- ").Append(term.sourceTerm).Append(" -> ").AppendLine(term.targetTerm);
        }
    }


    private static void AppendVoice(StringBuilder prompt, string? voiceSummary) {
        if (string.IsNullOrWhiteSpace(voiceSummary)) {
            return;
        }

        prompt.AppendLine();
        prompt.AppendLine("VOICE - how this book's human translator writes.");
        prompt.AppendLine(voiceSummary);
    }


    // The proofread passage for a translate run: source and translation shown side by side, both
    // lines under the same marker, so the model can compare them without the pair drifting apart.
    // A repair has no source to show - its proofread passage is just SegmentProtocol.Format of the
    // repaired text, the same wire format the pass itself already speaks.
    public static string BuildTranslateProofreadPassage(IReadOnlyList<string> source, IReadOnlyList<string> translation) {
        StringBuilder passage = new StringBuilder();

        for (int index = 0; index < translation.Count; index++) {
            string marker = SegmentProtocol.Marker(index);
            string sourceLine = index < source.Count ? source[index] : string.Empty;

            passage.Append(marker).Append(" SOURCE ").AppendLine(sourceLine.ReplaceLineEndings(" "));
            passage.Append(marker).Append(" TRANSLATION ").AppendLine(translation[index].ReplaceLineEndings(" "));
        }

        return passage.ToString().TrimEnd();
    }


    // Parsed leniently: a line that is not a recognisable "marker | problem" pair, or whose marker
    // falls outside the chapter, is dropped rather than failing the whole proofread - the chapter
    // this reads was already produced, and one stray line must not be allowed to lose every finding
    // that came with it.
    public static IReadOnlyList<ProofreadFinding> ParseProofreadFindings(string reply, int segmentCount) {
        List<ProofreadFinding> findings = [];

        foreach (string rawLine in reply.ReplaceLineEndings("\n").Split('\n')) {
            string line = rawLine.Trim();

            if (line.Length == 0 || line.Equals(NoneMarker, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            int? index = ReadFindingMarker(line, out string remainder);

            if (index is null || index.Value < 0 || index.Value >= segmentCount) {
                continue;
            }

            int pipe = remainder.IndexOf('|');

            if (pipe < 0) {
                continue;
            }

            string problem = remainder[(pipe + 1)..].Trim();

            if (problem.Length == 0) {
                continue;
            }

            findings.Add(new ProofreadFinding(index.Value, problem));
        }

        return findings;
    }


    private static int? ReadFindingMarker(string line, out string remainder) {
        remainder = string.Empty;

        if (line.Length < 4 || line[0] != Placeholders.Open || line[1] != '#') {
            return null;
        }

        int close = line.IndexOf(Placeholders.Close);

        if (close < 2) {
            return null;
        }

        if (!int.TryParse(line[2..close], out int index)) {
            return null;
        }

        remainder = line[(close + 1)..].Trim();

        return index;
    }


    // ---- shared, low-level parsing -------------------------------------------------------------

    private static bool Has(string text, string heading) {
        return text.Contains(heading, StringComparison.Ordinal);
    }


    // One line per name or term, stripped of a leading bullet if the model added one, with NONE and
    // blank lines dropped - the same leniency VoicePrompt already reads its own TERMS section with.
    private static List<string> ParseListLines(string section) {
        List<string> lines = [];

        foreach (string rawLine in section.Split('\n')) {
            string trimmed = rawLine.Trim().TrimStart('-').Trim();

            if (trimmed.Length == 0 || trimmed.Equals(NoneMarker, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            lines.Add(trimmed);
        }

        return lines;
    }


    private static string Section(string text, string heading, string? nextHeading) {
        int start = text.IndexOf(heading, StringComparison.Ordinal);

        if (start < 0) {
            return string.Empty;
        }

        start += heading.Length;

        int end = nextHeading is null ? -1 : text.IndexOf(nextHeading, start, StringComparison.Ordinal);

        return end < 0 ? text[start..] : text[start..end];
    }
}
