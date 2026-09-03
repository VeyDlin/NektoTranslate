using NektoTranslate.Common.Models;
using NektoTranslate.Glossary.Services;
using NektoTranslate.Translation.Contracts;
using Xunit;


namespace NektoTranslate.Tests.Glossary;


public class GlossaryUsageCheckerTests {

    private readonly GlossaryUsageChecker checker = new GlossaryUsageChecker(new EngineOptions());


    [Fact]
    public void AnEstablishedRenderingTheModelIgnoredIsReported() {
        IReadOnlyList<GlossaryTerm> ignored = checker.FindIgnored(
            [new GlossaryTerm("田中", "Танака", null)],
            "田中は眠っている。",
            "Он спит."
        );

        Assert.Equal("田中", Assert.Single(ignored).sourceTerm);
    }


    [Fact]
    public void ARenderingThatCameBackIsNotReported() {
        IReadOnlyList<GlossaryTerm> ignored = checker.FindIgnored(
            [new GlossaryTerm("田中", "Танака", null)],
            "田中は眠っている。",
            "Танака спит."
        );

        Assert.Empty(ignored);
    }


    [Fact]
    public void ATermAbsentFromThisChapterIsNotExpectedInItsTranslation() {
        IReadOnlyList<GlossaryTerm> ignored = checker.FindIgnored(
            [new GlossaryTerm("佐藤", "Сато", null)],
            "田中は眠っている。",
            "Танака спит."
        );

        Assert.Empty(ignored);
    }


    [Fact]
    public void AnyOfSeveralAcceptedFormsCounts() {
        IReadOnlyList<GlossaryTerm> ignored = checker.FindIgnored(
            [new GlossaryTerm("お兄ちゃん", "Онии-чан/Братик", null)],
            "「お兄ちゃん、もう朝だよ」",
            "«Братик, уже утро»"
        );

        Assert.Empty(ignored);
    }


    [Fact]
    public void InflectionDoesNotCountAsAMiss() {
        IReadOnlyList<GlossaryTerm> ignored = checker.FindIgnored(
            [new GlossaryTerm("田中", "Танак", null)],
            "田中は眠っている。",
            "Он разбудил Танака."
        );

        Assert.Empty(ignored);
    }
}
