using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using NektoTranslate.Common.Tools;


namespace NektoTranslate.Common.Controllers;


// A plain HTTP surface over the tool registry: list what exists, call one by name.
//
// Not MCP itself. It is the same two operations MCP's tools/list and tools/call provide, so adding
// that transport later means translating the envelope rather than rewriting the tools - and it
// makes the tools testable and usable from the interface today, without paying the cost of putting
// them in front of the translation model.
[ApiController]
[Route("api/tools")]
public class AgentToolsController(IAgentToolRegistry registry) : ControllerBase {

    [HttpGet]
    public IReadOnlyList<object> List() {
        return registry.All()
            .Select(tool => new {
                tool.name,
                tool.description,
                inputSchema = JsonDocument.Parse(tool.inputSchema).RootElement
            })
            .ToList<object>();
    }


    [HttpPost("{name}")]
    public async Task<ActionResult<JsonElement>> Invoke(
        string name,
        [FromBody] JsonElement arguments,
        CancellationToken cancellationToken
    ) {
        try {
            string result = await registry.InvokeAsync(name, arguments, cancellationToken);

            return JsonDocument.Parse(result).RootElement;
        } catch (UnknownAgentToolException) {
            return NotFound();
        }
    }
}
