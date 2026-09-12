using Microsoft.AspNetCore.Mvc;
using NektoTranslate.System.Contracts;
using NektoTranslate.System.Services;


namespace NektoTranslate.System.Controllers;


// What a browser user sees when there is no shell to check for them. The desktop shell's own
// updater (NektoTranslate.Desktop/src-tauri/src/updater.rs) never calls this - it reads the static
// latest.json a release attaches instead, since it needs the download URL and the signature this
// endpoint deliberately does not carry.
[ApiController]
[Route("api/system/update")]
public class UpdateController(IReleaseChecker releaseChecker) : ControllerBase {

    [HttpGet]
    public async Task<ActionResult<UpdateAvailability>> Get(CancellationToken cancellationToken) {
        return await releaseChecker.CheckAsync(cancellationToken);
    }
}
