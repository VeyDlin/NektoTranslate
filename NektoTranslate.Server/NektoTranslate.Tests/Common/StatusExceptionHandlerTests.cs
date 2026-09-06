using Microsoft.AspNetCore.Http;
using NektoTranslate.Common.Contracts;
using NektoTranslate.Common.Http;
using Xunit;


namespace NektoTranslate.Tests.Common;


// The interface shows whatever sentence comes back in the status, so the mapping from an escaped
// exception to a code and a sentence is the contract worth pinning: a refusal keeps the service's
// own words as the whole message, a failure keeps them too but says it is ours.
public class StatusExceptionHandlerTests {

    [Fact]
    public void ARefusalKeepsTheServicesOwnSentenceAsA409() {
        (int statusCode, Status status) = StatusExceptionHandler.Describe(
            new InvalidOperationException("Chapter 21 has no Russian translation yet. Repair rewrites an existing rendering; it does not create one.")
        );

        Assert.Equal(StatusCodes.Status409Conflict, statusCode);
        Assert.Equal("REQUEST_REFUSED", status.code);
        Assert.Equal("Chapter 21 has no Russian translation yet. Repair rewrites an existing rendering; it does not create one.", status.text);
    }


    [Fact]
    public void AnythingElseIsOurFailureWithTheReasonAttached() {
        (int statusCode, Status status) = StatusExceptionHandler.Describe(new IOException("disk full"));

        Assert.Equal(StatusCodes.Status500InternalServerError, statusCode);
        Assert.Equal("SERVER_FAILED", status.code);
        Assert.Contains("disk full", status.text);
        Assert.Equal("disk full", status.args!["reason"]);
    }
}
