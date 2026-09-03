using Microsoft.AspNetCore.Mvc;
using NektoTranslate.Settings.Contracts;
using NektoTranslate.Settings.Entities;
using NektoTranslate.Settings.Services;


namespace NektoTranslate.Settings.Controllers;


[ApiController]
[Route("api/settings")]
public class SettingsController(ISettingsService settings, IModelCatalog models) : ControllerBase {

    [HttpGet]
    public async Task<ApplicationSettings> Get(CancellationToken cancellationToken) {
        return await settings.GetAsync(cancellationToken);
    }


    [HttpPut]
    public async Task<ApplicationSettings> Update(
        [FromBody] UpdateSettingsRequest request,
        CancellationToken cancellationToken
    ) {
        return await settings.UpdateAsync(request, cancellationToken);
    }


    // The choices behind the model dropdown. Free, immediate, and safe to call on page load.
    [HttpGet("models")]
    public IReadOnlyList<ModelOption> Models() {
        return models.List();
    }


    // Whether this subscription can actually run a model. Spends a trivial amount when the model is
    // valid, so it belongs behind an explicit action rather than on page load.
    [HttpPost("models/{model}/probe")]
    public async Task<ModelProbeResult> Probe(string model, CancellationToken cancellationToken) {
        return await models.ProbeAsync(model, cancellationToken);
    }


    // What the configured local server reports it has loaded.
    [HttpGet("models/local")]
    public async Task<LocalModelList> LocalModels(CancellationToken cancellationToken) {
        return await models.ListLocalAsync(cancellationToken);
    }
}
