using System.Text.Json;


namespace NektoTranslate.Common.Tools;


// A capability the agent can be given, described the way MCP describes one: a name, a sentence of
// prose and a JSON Schema for its arguments.
//
// Transport-free on purpose. The same tool is called in-process today and can be published over an
// MCP endpoint tomorrow without being rewritten, because nothing here knows how the call arrived.
//
// Why the split matters in this application: exposing tools to the model costs roughly a hundred
// times more per request than not doing so, because tool definitions cannot reach Claude Code
// without also dragging its whole agent prompt back in. Bulk chapter translation therefore keeps
// using the glossary through precomputed prompt text, while the interactive agent - where calls
// are few and each one is worth its price - is what these tools are for.
public interface IAgentTool {

    string name { get; }

    string description { get; }

    // JSON Schema for the arguments, as MCP expects it.
    string inputSchema { get; }


    Task<string> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken = default);
}
