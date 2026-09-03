using NektoTranslate.Common.Models;
using NektoTranslate.Translation.Checks;
using NektoTranslate.Translation.Contracts;
using Xunit;


namespace NektoTranslate.Tests.Translation;


// Every check is exercised on more than one language pair, and specifically on a pair it must stay
// quiet for. A check that fires on every chapter of an English-to-German book is worse than no
// check, and that failure mode is invisible unless the quiet case is tested too.
public class TranslationCheckTests {

    // The code defaults, which is what the checks ran on before any of this was configurable - so
    // these tests still pin the shipped behaviour rather than a test-only tuning.
    private static readonly EngineOptions options = new EngineOptions();

    private readonly UntranslatedResidueCheck residue = new UntranslatedResidueCheck(options);

    private readonly UntranslatedBlockCheck untouched = new UntranslatedBlockCheck(options);


    [Fact]
    public void JapaneseLeftInsideRussianIsReported() {
        IReadOnlyList<TranslationIssue> issues = residue.Run(Context(
            "Japanese", "Russian",
            "田中はまだ眠っている。",
            "Танака ещё спал. 窓のカーテンを開けた"
        ));

        Assert.Contains("窓のカーテンを開けた", Assert.Single(issues).message);
    }


    // The residue check scans block by block so a finding can say where it is. Without the position
    // the interface can only tell the reader that something is wrong somewhere in the chapter, which
    // on a chapter of two hundred paragraphs is barely better than saying nothing.
    [Fact]
    public void ResidueReportsWhichBlockItIsIn() {
        IReadOnlyList<TranslationIssue> issues = residue.Run(Context(
            "Japanese", "Russian",
            "田中はまだ眠っている。\n妹が窓のカーテンを開けた。",
            "Танака всё ещё спал.\nСестра открыла 窓のカーテンを開けた"
        ));

        Assert.Equal(1, Assert.Single(issues).blockIndex);
    }


    [Fact]
    public void ACleanRussianTranslationIsQuiet() {
        IReadOnlyList<TranslationIssue> issues = residue.Run(Context(
            "Japanese", "Russian",
            "田中はまだ眠っている。",
            "Танака всё ещё спал."
        ));

        Assert.Empty(issues);
    }


    // The case the check must not ruin: two languages in the same script, where source-script
    // characters in the output are simply the output.
    [Fact]
    public void EnglishToGermanIsQuietBecauseTheScriptIsShared() {
        IReadOnlyList<TranslationIssue> issues = residue.Run(Context(
            "English", "German",
            "He opened the door and looked outside.",
            "Er öffnete die Tür und schaute hinaus."
        ));

        Assert.Empty(issues);
    }


    [Fact]
    public void RussianToEnglishIsCheckedInThatDirectionToo() {
        IReadOnlyList<TranslationIssue> issues = residue.Run(Context(
            "Russian", "English",
            "Иван открыл дверь и посмотрел наружу.",
            "Ivan opened the door and посмотрел наружу"
        ));

        Assert.NotEmpty(issues);
    }


    // A block the model copied instead of translating stays at its own index, because the segment
    // protocol puts each answer back where its question was. The check compares aligned positions
    // for that reason.
    [Fact]
    public void ABlockCopiedFromTheSourceIsReported() {
        IReadOnlyList<TranslationIssue> issues = untouched.Run(Context(
            "Japanese", "Russian",
            "田中はまだ眠っている。\n妹の美咲が窓のカーテンを開けた。",
            "Танака всё ещё спал.\n妹の美咲が窓のカーテンを開けた。"
        ));

        // Asserted on the field rather than on the wording. The position is what the interface acts
        // on - it is how the reader gets taken to the paragraph - so it belongs in a value a caller
        // can use, not in prose a caller would have to parse.
        Assert.Equal(1, Assert.Single(issues).blockIndex);
    }


    [Fact]
    public void TheCopiedBlockCheckStandsDownWhenTheScriptIsShared() {
        Assert.False(untouched.AppliesTo("English", "English"));

        IReadOnlyList<TranslationIssue> issues = untouched.Run(Context(
            "English", "German",
            "A perfectly ordinary sentence here.",
            "A perfectly ordinary sentence here."
        ));

        Assert.Empty(issues);
    }


    [Theory]
    [InlineData("田中はまだ眠っている", Script.Ideographic)]
    [InlineData("Танака всё ещё спал", Script.Cyrillic)]
    [InlineData("Tanaka was still asleep", Script.Latin)]
    [InlineData("김민수는 학교에 갔다", Script.Ideographic)]
    public void ScriptDetectionCoversTheLanguagesWeAccept(string text, Script expected) {
        Assert.Equal(expected, ScriptFamily.Detect(text));
    }


    private static TranslationCheckContext Context(
        string source,
        string target,
        string sourceText,
        string translatedText
    ) {
        return new TranslationCheckContext(source, target, sourceText, translatedText, []);
    }
}
