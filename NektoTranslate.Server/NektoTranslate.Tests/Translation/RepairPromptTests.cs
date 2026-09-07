using NektoTranslate.Glossary.Entities;
using NektoTranslate.Glossary.Enums;
using NektoTranslate.Glossary.Services;
using NektoTranslate.Translation.Entities;
using NektoTranslate.Translation.Services;
using Xunit;


namespace NektoTranslate.Tests.Translation;


// The parts of a repair that do not need the database or the model: which settled terms a
// chapter's own text mentions, how far a continuity tail reaches, and what the repair prompt tells
// the model. The last of those is the point of the whole feature - a repair works from an existing
// rendering with no original to check it against, and the prompt is where that limit either holds
// or quietly disappears.
public class RepairPromptTests {

    [Fact]
    public void ATermIsSelectedWhenItsCanonicalFormAppearsInTheChapter() {
        IReadOnlyList<RepairTerm> terms = RepairPrompt.MergeTerms([Term("Иван", "[]")], []);

        IReadOnlyList<RepairTerm> selected = RepairPrompt.SelectRelevantTerms(
            terms,
            "Иван вошёл в комнату.",
            TermMatching.DefaultMaxInflectionLength
        );

        Assert.Equal(["Иван"], selected.Select(term => term.term));
    }


    [Fact]
    public void ATermAbsentFromTheChapterIsNotSelected() {
        IReadOnlyList<RepairTerm> terms = RepairPrompt.MergeTerms([Term("Иван", "[]")], []);

        IReadOnlyList<RepairTerm> selected = RepairPrompt.SelectRelevantTerms(
            terms,
            "Сестра открыла занавеску.",
            TermMatching.DefaultMaxInflectionLength
        );

        Assert.Empty(selected);
    }


    // The canonical form is what the human translator wrote, and it need not itself appear for the
    // term to be relevant - an alternate spelling seen elsewhere still counts as the same term when
    // it turns up in this chapter, which is exactly why variantsJson is recorded at all.
    [Fact]
    public void AVariantAloneStillSelectsTheTerm() {
        // "Кобаяши" is not a prefix of "Кобаяси", so only the recorded variant can match here - the
        // canonical form alone would not.
        IReadOnlyList<RepairTerm> terms = RepairPrompt.MergeTerms([Term("Кобаяши", "[\"Кобаяси\"]")], []);

        IReadOnlyList<RepairTerm> selected = RepairPrompt.SelectRelevantTerms(
            terms,
            "Кобаяси вошёл в комнату.",
            TermMatching.DefaultMaxInflectionLength
        );

        Assert.Equal(["Кобаяши"], selected.Select(term => term.term));
    }


    // A row whose variants failed to store as valid JSON must not take down the whole repair - it is
    // matched on its canonical form alone instead of throwing.
    [Fact]
    public void MalformedVariantsJsonIsIgnoredRatherThanThrown() {
        IReadOnlyList<RepairTerm> terms = RepairPrompt.MergeTerms([Term("Иван", "not json")], []);

        IReadOnlyList<RepairTerm> selected = RepairPrompt.SelectRelevantTerms(
            terms,
            "Иван вошёл в комнату.",
            TermMatching.DefaultMaxInflectionLength
        );

        Assert.Equal(["Иван"], selected.Select(term => term.term));
    }


    [Fact]
    public void SelectedTermsComeBackLongestFirst() {
        IReadOnlyList<RepairTerm> terms = RepairPrompt.MergeTerms([Term("Ли", "[]"), Term("Александра", "[]")], []);

        IReadOnlyList<RepairTerm> selected = RepairPrompt.SelectRelevantTerms(
            terms,
            "Ли и Александра вышли вместе.",
            TermMatching.DefaultMaxInflectionLength
        );

        Assert.Equal(["Александра", "Ли"], selected.Select(term => term.term));
    }


    // A misspelled name is precisely the one thing the mention test cannot find - the reader notices
    // it before anything else - so a Person, Place or Organization must reach the prompt whether or
    // not this chapter's own text happens to mention it.
    [Fact]
    public void AnUnmentionedPersonReachesThePromptAnyway() {
        TranslationTerm term = Term("Уолтер Тай", "[]");
        term.category = GlossaryCategory.Person;

        IReadOnlyList<RepairTerm> terms = RepairPrompt.MergeTerms([term], []);

        IReadOnlyList<RepairTerm> selected = RepairPrompt.SelectRelevantTerms(
            terms,
            "Вальтер Тей вошёл в комнату.",
            TermMatching.DefaultMaxInflectionLength
        );

        Assert.Equal(["Уолтер Тай"], selected.Select(term => term.term));
    }


    // Everything that is not a Person, Place or Organization keeps the mention discipline exactly as
    // before - sending every item the book has ever settled would grow every request with the book
    // instead of with the chapter.
    [Fact]
    public void AnUnmentionedItemDoesNotReachThePrompt() {
        TranslationTerm term = Term("Меч Рассвета", "[]");
        term.category = GlossaryCategory.Item;

        IReadOnlyList<RepairTerm> terms = RepairPrompt.MergeTerms([term], []);

        IReadOnlyList<RepairTerm> selected = RepairPrompt.SelectRelevantTerms(
            terms,
            "В этой главе о нём не говорится.",
            TermMatching.DefaultMaxInflectionLength
        );

        Assert.Empty(selected);
    }


    // A name the chapter does mention must never be dropped by the cap for one it does not - the cap
    // exists to bound an unused roster, not to bump a name the chapter is actively getting wrong.
    [Fact]
    public void TheCapKeepsAMentionedRareNameOverAnUnmentionedFrequentOne() {
        TranslationTerm rare = Term("Ханако", "[]");
        rare.category = GlossaryCategory.Person;
        rare.occurrences = 1;

        TranslationTerm frequent = Term("Такеши", "[]");
        frequent.category = GlossaryCategory.Person;
        frequent.occurrences = 100;

        IReadOnlyList<RepairTerm> terms = RepairPrompt.MergeTerms([rare, frequent], []);

        IReadOnlyList<RepairTerm> selected = RepairPrompt.SelectRelevantTerms(
            terms,
            "Ханако вошла в комнату.",
            TermMatching.DefaultMaxInflectionLength,
            1
        );

        Assert.Equal(["Ханако"], selected.Select(term => term.term));
    }


    // A term the source-term pipeline settled on from a chapter that has its original is offered to
    // a repair exactly as a TranslationTerm would be, so a name learned there is enforced in a
    // chapter that has no original to check it against.
    [Fact]
    public void AGlossaryEntryMentionedInTheChapterReachesThePrompt() {
        IReadOnlyList<RepairTerm> terms = RepairPrompt.MergeTerms(
            [],
            [Entry("Ханако", "the younger sister")]
        );

        IReadOnlyList<RepairTerm> selected = RepairPrompt.SelectRelevantTerms(
            terms,
            "Ханако вошла в комнату.",
            TermMatching.DefaultMaxInflectionLength
        );

        string prompt = RepairPrompt.BuildSystemPrompt("Russian", null, selected, "", "", 1, 1, []);

        Assert.Contains("Ханако (the younger sister)", prompt);
    }


    // The two tables can settle the same rendering independently - one from a chapter with no
    // original, the other from the source-term pipeline reading a chapter that has one. The
    // TranslationTerm must win, because it is read off this book's own prose and carries the
    // variants actually seen there, which the GlossaryEntry never does.
    [Fact]
    public void ATranslationTermWinsTheDedupeOverAGlossaryEntryOfTheSameTerm() {
        GlossaryEntry entry = Entry("Иван", "from the glossary");
        TranslationTerm term = Term("Иван", "[\"Ваня\"]");
        term.notes = "from the translation";

        IReadOnlyList<RepairTerm> merged = RepairPrompt.MergeTerms([term], [entry]);

        RepairTerm result = Assert.Single(merged);
        Assert.Equal("Иван", result.term);
        Assert.Equal(["Ваня"], result.variants);
        Assert.Equal("from the translation", result.notes);
    }


    [Fact]
    public void TailKeepsOnlyTheLastParagraphsAndDropsBlankLines() {
        string plainText = "Первый.\n\nВторой.\n\nТретий.";

        string[] tail = RepairPrompt.Tail(plainText, 2);

        Assert.Equal(["Второй.", "Третий."], tail);
    }


    [Fact]
    public void TailNeverReturnsMoreThanIsThere() {
        string[] tail = RepairPrompt.Tail("Один.", 5);

        Assert.Equal(["Один."], tail);
    }


    // The one instruction this whole feature depends on: what the model may fix, and what it may
    // not invent back because there is no original left to check it against. If this line is ever
    // removed the prompt starts reading as a translation this class never claims to be.
    [Fact]
    public void ThePromptStatesTheModelMayNotRecoverLostMeaning() {
        string prompt = RepairPrompt.BuildSystemPrompt("Russian", null, [], "", "", 1, 1, []);

        Assert.Contains("no reliable original", prompt);
        Assert.Contains("must not invent, guess, or restore meaning", prompt, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public void ThePromptCarriesTheSegmentCountAndTheTargetLanguage() {
        string prompt = RepairPrompt.BuildSystemPrompt("Russian", null, [], "", "", 3, 1, []);

        Assert.Contains("Russian", prompt);
        Assert.Contains("exactly 3 segments", prompt);
    }


    [Fact]
    public void AVoiceProfileAddsItsSummaryAndItsAbsenceAddsNoSection() {
        VoiceProfile voice = new VoiceProfile {
            novelId = 1,
            language = "Russian",
            summary = "Short sentences, dry humour.",
            fromChapterIndex = 0,
            toChapterIndex = 18
        };

        string withVoice = RepairPrompt.BuildSystemPrompt("Russian", voice, [], "", "", 1, 1, []);
        string withoutVoice = RepairPrompt.BuildSystemPrompt("Russian", null, [], "", "", 1, 1, []);

        Assert.Contains("Short sentences, dry humour.", withVoice);
        Assert.DoesNotContain("VOICE", withoutVoice);
    }


    [Fact]
    public void KnownTermsAreListedWithTheirNotes() {
        TranslationTerm term = Term("Иван", "[]");
        term.notes = "the older brother";

        IReadOnlyList<RepairTerm> terms = RepairPrompt.MergeTerms([term], []);

        string prompt = RepairPrompt.BuildSystemPrompt("Russian", null, terms, "", "", 1, 1, []);

        Assert.Contains("Иван (the older brother)", prompt);
    }


    [Fact]
    public void ARetryAttemptRepeatsTheFormatWarning() {
        string firstAttempt = RepairPrompt.BuildSystemPrompt("Russian", null, [], "", "", 1, 1, []);
        string secondAttempt = RepairPrompt.BuildSystemPrompt("Russian", null, [], "", "", 1, 2, []);

        Assert.DoesNotContain("did not follow this format", firstAttempt);
        Assert.Contains("did not follow this format", secondAttempt);
    }


    [Fact]
    public void ContinuityAndContinuationTailsAreCarriedWhenPresent() {
        string prompt = RepairPrompt.BuildSystemPrompt(
            "Russian", null, [], "Конец прошлой главы.", "Конец прошлого куска.", 1, 1, []
        );

        Assert.Contains("Конец прошлой главы.", prompt);
        Assert.Contains("Конец прошлого куска.", prompt);
    }


    // ---- the careful pass's own additions ----

    [Fact]
    public void HowToReadEachParagraphIsAlwaysPresent() {
        string prompt = RepairPrompt.BuildSystemPrompt("Russian", null, [], "", "", 1, 1, []);

        Assert.Contains("HOW TO READ EACH PARAGRAPH", prompt);
        Assert.Contains("a change is a decision, not a reflex", prompt);
    }


    [Fact]
    public void NoMemoAddsNoThisChapterSection() {
        string prompt = RepairPrompt.BuildSystemPrompt("Russian", null, [], "", "", 1, 1, []);

        Assert.DoesNotContain("THIS CHAPTER", prompt);
    }


    [Fact]
    public void AMemoAddsItsRegisterUnderThisChapter() {
        CarefulPass.Memo memo = new CarefulPass.Memo("A tense chapter.", ["Иван"], []);

        string prompt = RepairPrompt.BuildSystemPrompt("Russian", null, [], "", "", 1, 1, [], memo);

        Assert.Contains("THIS CHAPTER", prompt);
        Assert.Contains("A tense chapter.", prompt);
        Assert.Contains("Иван", prompt);
    }


    [Fact]
    public void NoExamplesAddsNoExamplesSection() {
        string prompt = RepairPrompt.BuildSystemPrompt("Russian", null, [], "", "", 1, 1, []);

        Assert.DoesNotContain("EXAMPLES", prompt);
    }


    [Fact]
    public void ExamplesAreShownWhenGiven() {
        string prompt = RepairPrompt.BuildSystemPrompt(
            "Russian", null, [], "", "", 1, 1, [], null, ["Пример хорошего перевода из этой книги."]
        );

        Assert.Contains("EXAMPLES", prompt);
        Assert.Contains("Пример хорошего перевода из этой книги.", prompt);
    }


    private static TranslationTerm Term(string term, string variantsJson) {
        return new TranslationTerm {
            novelId = 1,
            language = "Russian",
            term = term,
            variantsJson = variantsJson
        };
    }


    private static GlossaryEntry Entry(string targetTerm, string? notes) {
        return new GlossaryEntry {
            novelId = 1,
            language = "Russian",
            sourceTerm = targetTerm,
            targetTerm = targetTerm,
            notes = notes
        };
    }
}
