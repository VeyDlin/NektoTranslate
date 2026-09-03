using NektoTranslate.Glossary.Enums;
using NektoTranslate.Glossary.Services;
using Xunit;


namespace NektoTranslate.Tests.Glossary;


public class GlossaryPrecedenceTests {

    [Fact]
    public void WhatTheExistingTranslationGivesBeatsWhatTheModelInvents() {
        Assert.True(GlossaryPrecedence.Outranks(
            GlossaryEntryOrigin.FromExistingTranslation,
            GlossaryEntryOrigin.AiExtracted
        ));
    }


    [Fact]
    public void AnInventedRenderingNeverDisplacesAnEstablishedOne() {
        Assert.False(GlossaryPrecedence.Outranks(
            GlossaryEntryOrigin.AiExtracted,
            GlossaryEntryOrigin.FromExistingTranslation
        ));
    }


    [Fact]
    public void AHandEditOutranksEverything() {
        Assert.True(GlossaryPrecedence.Outranks(
            GlossaryEntryOrigin.Manual,
            GlossaryEntryOrigin.FromExistingTranslation
        ));
        Assert.True(GlossaryPrecedence.Outranks(
            GlossaryEntryOrigin.Manual,
            GlossaryEntryOrigin.AiExtracted
        ));
    }


    [Fact]
    public void TheEarlierEntryStandsWhenBothCarryEqualWeight() {
        Assert.False(GlossaryPrecedence.Outranks(
            GlossaryEntryOrigin.FromExistingTranslation,
            GlossaryEntryOrigin.FromExistingTranslation
        ));
    }


    [Fact]
    public void OnlyAnInventedRenderingIsWorthReCheckingAgainstTheTranslation() {
        Assert.True(GlossaryPrecedence.IsOpenToRevision(GlossaryEntryOrigin.AiExtracted));
        Assert.False(GlossaryPrecedence.IsOpenToRevision(GlossaryEntryOrigin.FromExistingTranslation));
        Assert.False(GlossaryPrecedence.IsOpenToRevision(GlossaryEntryOrigin.Manual));
    }
}
