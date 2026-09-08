using Microsoft.AspNetCore.Mvc;
using NektoTranslate.System.Contracts;
using NektoTranslate.System.Controllers;
using Xunit;


namespace NektoTranslate.Tests.System;


// No database and nothing to mock: the controller reads a static field computed from its own
// assembly's attributes, so calling it directly is the whole test.
public class HealthControllerTests {

    [Fact]
    public void StatusIsOk() {
        HealthResponse response = GetResponse();

        Assert.Equal("ok", response.status);
    }


    [Fact]
    public void VersionIsNeverBlank() {
        HealthResponse response = GetResponse();

        Assert.False(string.IsNullOrWhiteSpace(response.version));
    }


    private static HealthResponse GetResponse() {
        HealthController controller = new HealthController();
        ActionResult<HealthResponse> result = controller.Get();

        Assert.NotNull(result.Value);

        return result.Value!;
    }
}
