using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using NektoTranslate.System.Contracts;
using NektoTranslate.System.Services;
using NektoTranslate.Tests.Common;
using Xunit;


namespace NektoTranslate.Tests.System;


public class GitHubReleaseCheckerTests {

    [Theory]
    [InlineData("v0.2.1", "0.2.0", true)]
    [InlineData("0.2.1", "0.2.0", true)]
    [InlineData("v0.3.0", "0.2.9", true)]
    [InlineData("v1.0.0", "0.9.9", true)]
    [InlineData("v0.2.0", "0.2.0", false)]
    [InlineData("v0.1.9", "0.2.0", false)]
    public void TryIsNewerComparesReleaseTagsAsVersions(string tag, string current, bool expected) {
        bool isNewer = GitHubReleaseChecker.TryIsNewer(tag, current, out string? latest);

        Assert.Equal(expected, isNewer);
        Assert.Equal(tag.TrimStart('v'), latest);
    }


    [Fact]
    public void TryIsNewerIsFalseForATagThatIsNotAVersion() {
        bool isNewer = GitHubReleaseChecker.TryIsNewer("vNext", "0.2.0", out _);

        Assert.False(isNewer);
    }


    [Fact]
    public void TryIsNewerIsFalseWhenTheRunningVersionItselfDoesNotParse() {
        bool isNewer = GitHubReleaseChecker.TryIsNewer("v0.2.1", "not-a-version", out _);

        Assert.False(isNewer);
    }


    [Fact]
    public void CoreVersionStripsABuildMetadataSuffix() {
        Assert.Equal("0.2.0", GitHubReleaseChecker.CoreVersion("0.2.0+abcdef1"));
    }


    [Fact]
    public void CoreVersionLeavesAPlainVersionUntouched() {
        Assert.Equal("0.2.0", GitHubReleaseChecker.CoreVersion("0.2.0"));
    }


    [Fact]
    public async Task CheckAsyncReadsTheLatestReleaseFromAWellFormedResponse() {
        FakeHttpMessageHandler handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent("""
                {
                  "tag_name": "v9.9.9",
                  "html_url": "https://github.com/VeyDlin/NektoTranslate/releases/tag/v9.9.9",
                  "published_at": "2026-01-01T00:00:00Z"
                }
                """),
        });

        GitHubReleaseChecker checker = new GitHubReleaseChecker(
            new FakeHttpClientFactory(handler),
            NullLogger<GitHubReleaseChecker>.Instance
        );

        UpdateAvailability result = await checker.CheckAsync();

        Assert.True(result.@checked);
        Assert.True(result.isNewer);
        Assert.Equal("9.9.9", result.latest);
        Assert.Equal("https://github.com/VeyDlin/NektoTranslate/releases/tag/v9.9.9", result.url);
        Assert.NotNull(result.publishedAt);
    }


    // GitHub could not be reached (network down, rate-limited, whatever the exception says) - the
    // contract is `checked: false` and `isNewer: false`, never an exception out of this method.
    [Fact]
    public async Task CheckAsyncIsCheckedFalseWhenGitHubCannotBeReached() {
        FakeHttpMessageHandler handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("offline"));

        GitHubReleaseChecker checker = new GitHubReleaseChecker(
            new FakeHttpClientFactory(handler),
            NullLogger<GitHubReleaseChecker>.Instance
        );

        UpdateAvailability result = await checker.CheckAsync();

        Assert.False(result.@checked);
        Assert.False(result.isNewer);
        Assert.Null(result.latest);
        Assert.Null(result.url);
        Assert.Null(result.publishedAt);
    }


    [Fact]
    public async Task CheckAsyncIsCheckedFalseOnANonSuccessStatusCode() {
        FakeHttpMessageHandler handler = new FakeHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        );

        GitHubReleaseChecker checker = new GitHubReleaseChecker(
            new FakeHttpClientFactory(handler),
            NullLogger<GitHubReleaseChecker>.Instance
        );

        UpdateAvailability result = await checker.CheckAsync();

        Assert.False(result.@checked);
    }


    // The cache is exactly what stops a request storm hitting GitHub once per open window - a
    // second call inside the six-hour window must not reach the handler at all.
    [Fact]
    public async Task CheckAsyncCachesTheAnswerRatherThanAskingGitHubAgain() {
        int calls = 0;

        FakeHttpMessageHandler handler = new FakeHttpMessageHandler(_ => {
            calls++;

            return new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent("""{"tag_name": "v0.1.0"}"""),
            };
        });

        GitHubReleaseChecker checker = new GitHubReleaseChecker(
            new FakeHttpClientFactory(handler),
            NullLogger<GitHubReleaseChecker>.Instance
        );

        await checker.CheckAsync();
        await checker.CheckAsync();

        Assert.Equal(1, calls);
    }
}
