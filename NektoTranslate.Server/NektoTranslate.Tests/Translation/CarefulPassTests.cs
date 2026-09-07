using System.Text;
using NektoTranslate.Glossary.Enums;
using NektoTranslate.Glossary.Services;
using NektoTranslate.Translation.Contracts;
using NektoTranslate.Translation.Services;
using Xunit;


namespace NektoTranslate.Tests.Translation;


// CarefulPass needs neither the database nor the model - it only builds the prompts a careful pass
// sends and reads what comes back - so every piece of it is worth asserting on directly: the batch
// prompt's context block, the memo's two shapes and their lenient parsing, and the proofread's
// format and its lenient parsing of what the model flags.
public class CarefulPassTests {

    // ---- batching with context ----

    [Fact]
    public void ABatchWithNoContextReadsExactlyAsTheOrdinarySegmentProtocolDoes() {
        string prompt = CarefulPass.BuildBatchPrompt([], ["первый", "второй"], []);

        Assert.Equal(SegmentProtocol.Format(["первый", "второй"]), prompt);
    }


    [Fact]
    public void ContextLinesAreMarkedAndSitUnderTheReadingOnlyHeading() {
        string prompt = CarefulPass.BuildBatchPrompt(["до"], ["тело"], ["после"]);

        Assert.Contains("for reading only - do not translate, repair or output these lines", prompt);
        Assert.Contains("(context) до", prompt);
        Assert.Contains("(context) после", prompt);
    }


    [Fact]
    public void TheBodyKeepsItsOwnMarkersUnprefixed() {
        string prompt = CarefulPass.BuildBatchPrompt(["до"], ["тело"], []);

        Assert.Contains("⟦#0⟧тело", prompt);
        Assert.DoesNotContain("(context) тело", prompt);
    }


    [Fact]
    public void NoContextBeforeAddsOnlyOneHeadingForTheContextAfterIt() {
        string prompt = CarefulPass.BuildBatchPrompt([], ["тело"], ["после"]);

        // Split on the heading yields two pieces for one occurrence, three for two.
        Assert.Equal(2, prompt.Split(CarefulPass.ContextHeading).Length);
    }


    // ---- the memo: translate ----

    [Fact]
    public void ATranslateMemoPromptListsTheOfferedTermsAndStatesTheSections() {
        string prompt = CarefulPass.BuildTranslateMemoPrompt(
            "Japanese",
            "Russian",
            [new GlossaryTerm("田中", "Танака", null)]
        );

        Assert.Contains("REGISTER", prompt);
        Assert.Contains("PRESENT", prompt);
        Assert.Contains("田中", prompt);
        Assert.DoesNotContain("MISSPELLED", prompt);
    }


    [Fact]
    public void ATranslateMemoParsesRegisterAndPresentTerms() {
        string reply = "REGISTER\nTwo siblings, casual tone.\n\nPRESENT\n田中\n山田";

        CarefulPass.Memo memo = CarefulPass.ParseTranslateMemo(reply);

        Assert.Equal("Two siblings, casual tone.", memo.register);
        Assert.Equal(["田中", "山田"], memo.present);
        Assert.Empty(memo.misspelled);
    }


    [Fact]
    public void ATranslateMemoWithNoPresentTermsParsesAnEmptyList() {
        CarefulPass.Memo memo = CarefulPass.ParseTranslateMemo("REGISTER\nQuiet chapter.\n\nPRESENT\nNONE");

        Assert.Empty(memo.present);
    }


    // ---- the memo: repair ----

    [Fact]
    public void ARepairMemoPromptListsEstablishedNamesAndAsksForMisspellings() {
        string prompt = CarefulPass.BuildRepairMemoPrompt(
            "Russian",
            [new RepairTerm("Уолтер Тай", [], null, GlossaryCategory.Person, 0, null)]
        );

        Assert.Contains("REGISTER", prompt);
        Assert.Contains("PRESENT", prompt);
        Assert.Contains("MISSPELLED", prompt);
        Assert.Contains("Уолтер Тай", prompt);
        Assert.Contains("written in the chapter | established rendering", prompt);
    }


    [Fact]
    public void ARepairMemoParsesAllThreeSections() {
        RepairTerm term = new RepairTerm("Уолтер Тай", [], null, GlossaryCategory.Person, 0, null);
        string reply =
            "REGISTER\nA tense chapter.\n\n"
            + "PRESENT\nУолтер Тай\n\n"
            + "MISSPELLED\nВальтер Тей | Уолтер Тай";

        CarefulPass.Memo memo = CarefulPass.ParseRepairMemo(
            reply,
            [term],
            "Вальтер Тей вошёл в комнату.",
            TermMatching.DefaultMaxInflectionLength
        );

        Assert.Equal("A tense chapter.", memo.register);
        Assert.Equal(["Уолтер Тай"], memo.present);
        NameAlignment alignment = Assert.Single(memo.misspelled);
        Assert.Equal("Вальтер Тей", alignment.written);
        Assert.Equal("Уолтер Тай", alignment.established);
    }


    // The parser for MISSPELLED is RepairPrompt.ParseNameAlignments itself, unchanged - a pair
    // naming an established name the memo was never offered must be dropped exactly as it already
    // was for the old per-batch alignment pass.
    [Fact]
    public void ARepairMemoDropsAMisspellingAgainstAnUnknownEstablishedName() {
        RepairTerm term = new RepairTerm("Уолтер Тай", [], null, GlossaryCategory.Person, 0, null);
        string reply = "REGISTER\n.\n\nPRESENT\nNONE\n\nMISSPELLED\nВальтер Тей | Кто-то Другой";

        CarefulPass.Memo memo = CarefulPass.ParseRepairMemo(
            reply,
            [term],
            "Вальтер Тей вошёл в комнату.",
            TermMatching.DefaultMaxInflectionLength
        );

        Assert.Empty(memo.misspelled);
    }


    [Fact]
    public void ANoneAnswerUnderMisspelledYieldsNoAlignments() {
        RepairTerm term = new RepairTerm("Уолтер Тай", [], null, GlossaryCategory.Person, 0, null);
        string reply = "REGISTER\n.\n\nPRESENT\nNONE\n\nMISSPELLED\nNONE";

        CarefulPass.Memo memo = CarefulPass.ParseRepairMemo(reply, [term], "текст", TermMatching.DefaultMaxInflectionLength);

        Assert.Empty(memo.misspelled);
    }


    // ---- lenient parsing of a malformed memo ----

    [Fact]
    public void AReplyWithNoRecognisableHeadingIsKeptWholeAsFreeText() {
        CarefulPass.Memo memo = CarefulPass.ParseTranslateMemo("Just some prose about the chapter.");

        Assert.Equal("Just some prose about the chapter.", memo.register);
        Assert.Empty(memo.present);
    }


    [Fact]
    public void AnEmptyReplyParsesToTheEmptyMemo() {
        Assert.Equal(CarefulPass.Memo.Empty, CarefulPass.ParseTranslateMemo(""));
        Assert.Equal(CarefulPass.Memo.Empty, CarefulPass.ParseTranslateMemo("   "));
    }


    // ---- THIS CHAPTER ----

    [Fact]
    public void ThisChapterCarriesTheRegisterAndThePresentTerms() {
        StringBuilder prompt = new StringBuilder();
        CarefulPass.AppendThisChapter(prompt, new CarefulPass.Memo("Tense and quiet.", ["Иван"], []));

        string text = prompt.ToString();

        Assert.Contains("THIS CHAPTER", text);
        Assert.Contains("Tense and quiet.", text);
        Assert.Contains("Иван", text);
    }


    // ---- how to read each paragraph ----

    [Fact]
    public void TranslateHowToReadNamesTheThreeQuestionsAndAddsItsOwnSentence() {
        StringBuilder prompt = new StringBuilder();
        CarefulPass.AppendHowToReadForTranslate(prompt, "Japanese", "Russian");

        string text = prompt.ToString();

        Assert.Contains("HOW TO READ EACH PARAGRAPH", text);
        Assert.Contains("translate the meaning, not the sentence", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("is rebuilt, not mirrored", text);
    }


    [Fact]
    public void RepairHowToReadAddsTheReflexSentence() {
        StringBuilder prompt = new StringBuilder();
        CarefulPass.AppendHowToReadForRepair(prompt, "Russian");

        string text = prompt.ToString();

        Assert.Contains("HOW TO READ EACH PARAGRAPH", text);
        Assert.Contains("a change is a decision, not a reflex", text);
    }


    // ---- examples ----

    [Fact]
    public void ExampleSelectionDropsShortParagraphsAndCapsAtThree() {
        IReadOnlyList<string> selected = CarefulPass.SelectExampleParagraphs([
            "too short",
            "This paragraph is long enough to count as a real example of the voice.",
            "This one is also long enough to count as a second real example here.",
            "And this is a third paragraph long enough to be a good third example.",
            "This fourth one would also qualify, but only three are ever taken at most."
        ]);

        Assert.Equal(3, selected.Count);
        Assert.DoesNotContain("too short", selected);
    }


    [Fact]
    public void NoExamplesAddsNoExamplesBlock() {
        StringBuilder prompt = new StringBuilder();
        CarefulPass.AppendExamples(prompt, []);

        Assert.Equal(string.Empty, prompt.ToString());
    }


    // ---- the proofread: translate ----

    [Fact]
    public void ATranslateProofreadSystemPromptMentionsTheGlossaryAndTheMarkerFormat() {
        string prompt = CarefulPass.BuildTranslateProofreadSystemPrompt(
            "Japanese",
            "Russian",
            3,
            [new GlossaryTerm("田中", "Танака", null)],
            "Short sentences."
        );

        Assert.Contains("⟦#0⟧ | problem", prompt);
        Assert.Contains("田中", prompt);
        Assert.Contains("Short sentences.", prompt);
        Assert.Contains("NONE", prompt);
    }


    [Fact]
    public void TranslateProofreadPassageMarksBothSidesWithTheSameMarker() {
        string passage = CarefulPass.BuildTranslateProofreadPassage(["源"], ["перевод"]);

        Assert.Contains("⟦#0⟧ SOURCE 源", passage);
        Assert.Contains("⟦#0⟧ TRANSLATION перевод", passage);
    }


    // ---- the proofread: repair ----

    [Fact]
    public void ARepairProofreadSystemPromptHasNoSourceLanguageAndStillListsTheGlossary() {
        RepairTerm term = new RepairTerm("Иван", [], null, GlossaryCategory.Person, 0, null);
        string prompt = CarefulPass.BuildRepairProofreadSystemPrompt("Russian", 2, [term], null);

        Assert.Contains("no reliable original", prompt);
        Assert.Contains("Иван", prompt);
        Assert.Contains("⟦#0⟧ | problem", prompt);
    }


    [Fact]
    public void RepairProofreadPassageIsPlainSegmentFormat() {
        string passage = SegmentProtocol.Format(["первый", "второй"]);

        Assert.Equal("⟦#0⟧первый\n⟦#1⟧второй", passage.ReplaceLineEndings("\n"));
    }


    // ---- proofread findings parsing ----

    [Fact]
    public void AWellFormedFindingLineIsParsed() {
        IReadOnlyList<CarefulPass.ProofreadFinding> findings = CarefulPass.ParseProofreadFindings(
            "⟦#1⟧ | reads as a calque",
            3
        );

        CarefulPass.ProofreadFinding finding = Assert.Single(findings);
        Assert.Equal(1, finding.segmentIndex);
        Assert.Equal("reads as a calque", finding.problem);
    }


    [Fact]
    public void NoneAnswersWithNoFindings() {
        Assert.Empty(CarefulPass.ParseProofreadFindings("NONE", 3));
    }


    [Fact]
    public void ALineWhoseSegmentIsOutOfRangeIsDropped() {
        Assert.Empty(CarefulPass.ParseProofreadFindings("⟦#5⟧ | some problem", 3));
    }


    [Fact]
    public void ALineWithNoPipeIsDropped() {
        Assert.Empty(CarefulPass.ParseProofreadFindings("⟦#0⟧ needs work", 3));
    }


    [Fact]
    public void ALineWithAnEmptyProblemIsDropped() {
        Assert.Empty(CarefulPass.ParseProofreadFindings("⟦#0⟧ | ", 3));
    }


    [Fact]
    public void MultipleFindingLinesAllParse() {
        IReadOnlyList<CarefulPass.ProofreadFinding> findings = CarefulPass.ParseProofreadFindings(
            "⟦#0⟧ | tense drift\n⟦#2⟧ | name spelled differently",
            3
        );

        Assert.Equal(2, findings.Count);
        Assert.Equal([0, 2], findings.Select(finding => finding.segmentIndex));
    }
}
