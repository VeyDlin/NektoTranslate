using NektoTranslate.Settings.Services;
using Xunit;


namespace NektoTranslate.Tests.Settings;


// The one rule the whole feature depends on: a pass call thinks and a mechanical call never does.
// Every caller either passes ApplicationSettings.thinkingTokens through this function (a pass) or
// never calls it at all (a mechanical call), so this is where the rule can be asserted directly.
public class ThinkingEnvironmentTests {

    [Fact]
    public void APositiveBudgetSetsTheThinkingVariable() {
        Dictionary<string, string?>? variables = ThinkingEnvironment.BuildEnvironmentVariables(6000);

        Assert.NotNull(variables);
        Assert.Equal("6000", variables[ThinkingEnvironment.ThinkingTokensVariable]);
    }


    [Fact]
    public void AZeroBudgetSetsNothing() {
        Assert.Null(ThinkingEnvironment.BuildEnvironmentVariables(0));
    }


    [Fact]
    public void ANegativeBudgetSetsNothing() {
        Assert.Null(ThinkingEnvironment.BuildEnvironmentVariables(-1));
    }
}
