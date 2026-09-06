using NektoTranslate.Glossary.Enums;
using NektoTranslate.Glossary.Services;
using NektoTranslate.Translation.Services;
using Xunit;


namespace NektoTranslate.Tests.Translation;


// The "who is who" pass that runs before a repair rewrites a chapter: the prompt that hands the
// model every established name in the book, and the parser that decides which of its answers are
// trustworthy enough to feed back into the rewrite. A pair this parser accepts is one the rewrite
// will be told to enforce everywhere it appears, so what it lets through matters as much as what the
// rewrite prompt itself may not invent.
public class NameAlignmentTests {

    [Fact]
    public void ThePromptNamesEveryOfferedNameAndStatesTheFormat() {
        string prompt = RepairPrompt.BuildNameAlignmentSystemPrompt("Russian", [Name("Уолтер Тай"), Name("Ханако")]);

        Assert.Contains("Уолтер Тай", prompt);
        Assert.Contains("Ханако", prompt);
        Assert.Contains("written in the passage | established rendering", prompt);
    }


    [Fact]
    public void AValidPairIsAccepted() {
        IReadOnlyList<NameAlignment> alignments = RepairPrompt.ParseNameAlignments(
            "Вальтер Тей | Уолтер Тай",
            [Name("Уолтер Тай")],
            "Вальтер Тей вошёл в комнату.",
            TermMatching.DefaultMaxInflectionLength
        );

        NameAlignment alignment = Assert.Single(alignments);
        Assert.Equal("Вальтер Тей", alignment.written);
        Assert.Equal("Уолтер Тай", alignment.established);
    }


    [Fact]
    public void APairWithAnUnknownEstablishedNameIsDropped() {
        IReadOnlyList<NameAlignment> alignments = RepairPrompt.ParseNameAlignments(
            "Вальтер Тей | Кто-то Другой",
            [Name("Уолтер Тай")],
            "Вальтер Тей вошёл в комнату.",
            TermMatching.DefaultMaxInflectionLength
        );

        Assert.Empty(alignments);
    }


    // A model that invents a misspelling must not make the rewrite hunt for it - written has to
    // actually occur in the passage the pair was read from.
    [Fact]
    public void APairWhoseWrittenFormIsAbsentFromThePassageIsDropped() {
        IReadOnlyList<NameAlignment> alignments = RepairPrompt.ParseNameAlignments(
            "Вальтер Тей | Уолтер Тай",
            [Name("Уолтер Тай")],
            "Совсем другой текст без этого имени.",
            TermMatching.DefaultMaxInflectionLength
        );

        Assert.Empty(alignments);
    }


    [Fact]
    public void APairAlreadySpelledCorrectlyIsDropped() {
        IReadOnlyList<NameAlignment> alignments = RepairPrompt.ParseNameAlignments(
            "Уолтер Тай | Уолтер Тай",
            [Name("Уолтер Тай")],
            "Уолтер Тай вошёл в комнату.",
            TermMatching.DefaultMaxInflectionLength
        );

        Assert.Empty(alignments);
    }


    [Fact]
    public void NoneAnswersWithNoAlignments() {
        IReadOnlyList<NameAlignment> alignments = RepairPrompt.ParseNameAlignments(
            "NONE",
            [Name("Уолтер Тай")],
            "Уолтер Тай вошёл в комнату.",
            TermMatching.DefaultMaxInflectionLength
        );

        Assert.Empty(alignments);
    }


    [Fact]
    public void RepeatedWrittenFormsAreDeduped() {
        IReadOnlyList<NameAlignment> alignments = RepairPrompt.ParseNameAlignments(
            "Вальтер Тей | Уолтер Тай\nВальтер Тей | Уолтер Тай",
            [Name("Уолтер Тай")],
            "Вальтер Тей встретил Вальтер Тей снова.",
            TermMatching.DefaultMaxInflectionLength
        );

        Assert.Single(alignments);
    }


    // A line the model added as commentary, or that carries a stray extra "|", is not a pair this
    // parser can trust - only a line with exactly one "|" is read at all.
    [Fact]
    public void ALineWithoutExactlyOnePipeIsIgnored() {
        IReadOnlyList<NameAlignment> alignments = RepairPrompt.ParseNameAlignments(
            "Вальтер Тей spotted, no established match\nВальтер Тей | Уолтер Тай | extra",
            [Name("Уолтер Тай")],
            "Вальтер Тей вошёл в комнату.",
            TermMatching.DefaultMaxInflectionLength
        );

        Assert.Empty(alignments);
    }


    [Fact]
    public void BuildSystemPromptShowsTheNamesBlockOnlyWhenAlignmentsExist() {
        NameAlignment alignment = new NameAlignment("Вальтер Тей", "Уолтер Тай");

        string withAlignments = RepairPrompt.BuildSystemPrompt("Russian", null, [], "", "", 1, 1, [alignment]);
        string withoutAlignments = RepairPrompt.BuildSystemPrompt("Russian", null, [], "", "", 1, 1, []);

        Assert.Contains("NAMES IN THIS CHAPTER", withAlignments);
        Assert.Contains("\"Вальтер Тей\" -> Уолтер Тай", withAlignments);
        Assert.DoesNotContain("NAMES IN THIS CHAPTER", withoutAlignments);
    }


    private static RepairTerm Name(string term) {
        return new RepairTerm(term, [], null, GlossaryCategory.Person, 0, null);
    }
}
