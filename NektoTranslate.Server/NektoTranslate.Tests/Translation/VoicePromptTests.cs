using NektoTranslate.Glossary.Enums;
using NektoTranslate.Translation.Services;
using Xunit;


namespace NektoTranslate.Tests.Translation;


// The pure half of voice learning: parsing a chunk-level reply, folding chunk findings into one
// running term list, and counting how a term actually occurs in the sampled prose. Nothing here
// touches a database or a model, which is exactly why it is worth pinning down directly - this is
// where a name would quietly get merged into the wrong entry or counted twice across a whole book.
public class VoicePromptTests {

    [Fact]
    public void ParseReadsBothSectionsOfAWellFormedReply() {
        string reply =
            "VOICE:\n"
            + "Short, clipped sentences. Dialogue uses em-dashes rather than quotation marks.\n"
            + "\n"
            + "TERMS:\n"
            + "Иван | Person | Ивана, Ивану | the older brother\n";

        ChunkExtraction extraction = VoicePrompt.Parse(reply);

        Assert.Equal(
            "Short, clipped sentences. Dialogue uses em-dashes rather than quotation marks.",
            extraction.voiceNotes
        );
        ExtractedTerm term = Assert.Single(extraction.terms);
        Assert.Equal("Иван", term.term);
        Assert.Equal(GlossaryCategory.Person, term.category);
        Assert.Equal(["Ивана", "Ивану"], term.variants);
        Assert.Equal("the older brother", term.notes);
    }


    [Fact]
    public void ATermLineMayOmitVariantsAndNotes() {
        string reply = "VOICE:\nPlain register.\n\nTERMS:\nМидори | Place\n";

        ChunkExtraction extraction = VoicePrompt.Parse(reply);

        ExtractedTerm term = Assert.Single(extraction.terms);
        Assert.Equal("Мидори", term.term);
        Assert.Empty(term.variants);
        Assert.Null(term.notes);
    }


    [Fact]
    public void AnUnrecognisedCategoryFallsBackToOther() {
        string reply = "VOICE:\nPlain.\n\nTERMS:\nЗахир | Kingdom of nothing in particular\n";

        ExtractedTerm term = Assert.Single(VoicePrompt.Parse(reply).terms);

        Assert.Equal(GlossaryCategory.Other, term.category);
    }


    [Fact]
    public void NoneUnderTermsProducesNoTerms() {
        string reply = "VOICE:\nPlain register, short sentences.\n\nTERMS:\nNONE\n";

        Assert.Empty(VoicePrompt.Parse(reply).terms);
    }


    [Fact]
    public void ABlankLeadingFieldIsSkippedRatherThanKeptAsAnEmptyTerm() {
        string reply = "VOICE:\nPlain.\n\nTERMS:\n | Person | | \nИван | Person\n";

        ExtractedTerm term = Assert.Single(VoicePrompt.Parse(reply).terms);
        Assert.Equal("Иван", term.term);
    }


    [Fact]
    public void AVariantEqualToTheTermItselfIsDropped() {
        string reply = "VOICE:\nPlain.\n\nTERMS:\nИван | Person | Иван, Ивана\n";

        ExtractedTerm term = Assert.Single(VoicePrompt.Parse(reply).terms);

        Assert.Equal(["Ивана"], term.variants);
    }


    [Fact]
    public void AMissingVoiceSectionLeavesVoiceNotesEmptyRatherThanThrowing() {
        ChunkExtraction extraction = VoicePrompt.Parse("TERMS:\nNONE\n");

        Assert.Equal(string.Empty, extraction.voiceNotes);
        Assert.Empty(extraction.terms);
    }


    [Fact]
    public void AMissingTermsSectionLeavesTermsEmptyRatherThanThrowing() {
        ChunkExtraction extraction = VoicePrompt.Parse("VOICE:\nShort sentences throughout.\n");

        Assert.Equal("Short sentences throughout.", extraction.voiceNotes);
        Assert.Empty(extraction.terms);
    }


    [Fact]
    public void MergeTermsUnionsVariantsAcrossChunksForTheSameTerm() {
        Dictionary<string, MergedTerm> merged = new(StringComparer.Ordinal);

        VoicePrompt.MergeTerms(merged, [new ExtractedTerm("Иван", GlossaryCategory.Person, ["Ивана"], null)], chapterId: 10);
        VoicePrompt.MergeTerms(merged, [new ExtractedTerm("Иван", GlossaryCategory.Person, ["Ивану"], null)], chapterId: 20);

        MergedTerm term = Assert.Single(merged.Values);
        Assert.Equal(new HashSet<string> { "Ивана", "Ивану" }, term.variants);
    }


    // The chapter and category a term is credited with come from wherever it was first read - a
    // later chunk mentioning the same term again must not rewrite either, or a term learned from
    // chapter one would end up crediting chapter five simply because that batch happened to run
    // second.
    [Fact]
    public void MergeTermsKeepsTheFirstChapterAndCategorySeen() {
        Dictionary<string, MergedTerm> merged = new(StringComparer.Ordinal);

        VoicePrompt.MergeTerms(merged, [new ExtractedTerm("Иван", GlossaryCategory.Person, [], null)], chapterId: 10);
        VoicePrompt.MergeTerms(merged, [new ExtractedTerm("Иван", GlossaryCategory.Other, [], null)], chapterId: 20);

        MergedTerm term = Assert.Single(merged.Values);
        Assert.Equal(10, term.firstSeenChapterId);
        Assert.Equal(GlossaryCategory.Person, term.category);
    }


    [Fact]
    public void MergeTermsFillsInNotesFromWhicheverChunkOfferedThemFirst() {
        Dictionary<string, MergedTerm> merged = new(StringComparer.Ordinal);

        VoicePrompt.MergeTerms(merged, [new ExtractedTerm("Иван", GlossaryCategory.Person, [], null)], chapterId: 10);
        VoicePrompt.MergeTerms(merged, [new ExtractedTerm("Иван", GlossaryCategory.Person, [], "the older brother")], chapterId: 20);

        Assert.Equal("the older brother", merged.Values.Single().notes);
    }


    // The case this whole function exists for: a canonical form that is a prefix of one of its own
    // recorded variants. Counting both under TermMatching's tolerant, inflection-aware rule would
    // count every "Ивана" twice - once as itself, once again as part of a loose match on "Иван".
    [Fact]
    public void ACanonicalTermAndAVariantThatContainsItAreNotDoubleCounted() {
        string haystack = "Иван вошёл. Ивана позвали. Иван промолчал.";

        int occurrences = VoicePrompt.CountOccurrences(haystack, "Иван", ["Ивана"]);

        Assert.Equal(3, occurrences);
    }


    [Fact]
    public void WholeWordCountingDoesNotMatchInsideALongerUnrelatedWord() {
        // "Анна" must not be found inside "Анкара" or any other unrelated word that merely starts
        // the same way.
        int occurrences = VoicePrompt.CountOccurrences("Анкара - большой город. Анна приехала.", "Анна", []);

        Assert.Equal(1, occurrences);
    }


    [Fact]
    public void CountingIsCaseInsensitiveForAlphabeticScripts() {
        int occurrences = VoicePrompt.CountOccurrences("Ivan spoke. IVAN left. ivan returned.", "Ivan", []);

        Assert.Equal(3, occurrences);
    }


    [Fact]
    public void IdeographicTermsAreCountedByPlainSubstringSinceThereAreNoWordBoundaries() {
        int occurrences = VoicePrompt.CountOccurrences("田中さんは田中家の長男だ。", "田中", []);

        Assert.Equal(2, occurrences);
    }


    [Fact]
    public void SplitParagraphsDropsBlankLinesAndNormalisesLineEndings() {
        string plainText = "Первый.\r\n\r\nВторой.\n\nТретий.";

        Assert.Equal(["Первый.", "Второй.", "Третий."], VoicePrompt.SplitParagraphs(plainText));
    }


    [Fact]
    public void DecodeVariantsIsToleratedWhenTheStoredJsonIsMalformed() {
        Assert.Empty(VoicePrompt.DecodeVariants("not json"));
    }


    [Fact]
    public void DecodeVariantsReadsBackWhatWasEncoded() {
        HashSet<string> decoded = VoicePrompt.DecodeVariants("[\"Ивана\",\"Ивану\"]");

        Assert.Equal(new HashSet<string> { "Ивана", "Ивану" }, decoded);
    }


    [Fact]
    public void TheChunkPromptNamesTheTargetLanguageAndBothSectionHeadings() {
        string prompt = VoicePrompt.BuildChunkSystemPrompt("Russian");

        Assert.Contains("Russian", prompt);
        Assert.Contains("VOICE:", prompt);
        Assert.Contains("TERMS:", prompt);
    }


    // The one thing a chunk-level prompt must never claim: that there is an original somewhere to
    // check the passage against. If this line disappears the prompt starts reading like a
    // translation task instead of a read-only one.
    [Fact]
    public void TheChunkPromptStatesThereIsNoOriginalToCompareAgainst() {
        string prompt = VoicePrompt.BuildChunkSystemPrompt("Russian");

        Assert.Contains("no original text", prompt, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public void TheSynthesisPromptNamesTheTargetLanguage() {
        Assert.Contains("Russian", VoicePrompt.BuildSynthesisSystemPrompt("Russian"));
    }


    // A translator's typography - skills in square brackets, system messages set apart - is part of
    // the voice, and a model asked only about register and rhythm leaves it out. Both prompts have to
    // ask for it by name: the chunk prompt to notice it, the synthesis prompt to keep it.
    [Fact]
    public void BothPromptsAskForTheFormattingConventions() {
        Assert.Contains("formatting conventions", VoicePrompt.BuildChunkSystemPrompt("Russian"));
        Assert.Contains("Formatting conventions", VoicePrompt.BuildSynthesisSystemPrompt("Russian"));
    }


    // The profile is read on a screen and pasted into every request that matches it. Asked for one
    // paragraph, a model once answered with eight thousand characters and no line break; the prompt
    // has to pin both the shape - headings, blank lines between sections - and the length.
    [Fact]
    public void TheSynthesisPromptAsksForAShortSheetUnderHeadings() {
        string prompt = VoicePrompt.BuildSynthesisSystemPrompt("Russian");

        Assert.Contains("under 300 words", prompt);
        Assert.Contains("Register;", prompt);
        Assert.Contains("blank line between sections", prompt);
    }
}
