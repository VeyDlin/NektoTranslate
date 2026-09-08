using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using NektoTranslate.System.Contracts;


namespace NektoTranslate.System.Controllers;


// No database access on purpose. Migrations run before the host starts listening at all, so the
// first 200 from this endpoint already means the server is fully ready, not just that the process
// exists - a shell can poll it in a tight loop instead of guessing how long startup takes.
[ApiController]
[Route("api/health")]
public class HealthController : ControllerBase {

    // Read once per process rather than reflected over on every poll - the whole point of this
    // endpoint is answering fast enough for a shell to sit in a loop on it.
    private static readonly string version = typeof(HealthController).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion
        ?? "unknown";


    [HttpGet]
    public ActionResult<HealthResponse> Get() {
        return new HealthResponse("ok", version);
    }
}
