using System.Text.Json;


namespace NektoTranslate.Common.Tools;


public interface IAgentToolRegistry {

    IReadOnlyList<IAgentTool> All();


    IAgentTool? Find(string name);


    Task<string> InvokeAsync(
        string name,
        JsonElement arguments,
        CancellationToken cancellationToken = default
    );
}


// The one place that knows which tools exist. Everything registers itself through DI, so adding a
// capability is adding a class, and an MCP transport added later only has to walk this list rather
// than learn about each tool individually.
public class AgentToolRegistry(IEnumerable<IAgentTool> tools) : IAgentToolRegistry {

    private readonly Dictionary<string, IAgentTool> byName =
        tools.ToDictionary(tool => tool.name, StringComparer.Ordinal);


    public IReadOnlyList<IAgentTool> All() {
        return byName.Values.OrderBy(tool => tool.name, StringComparer.Ordinal).ToList();
    }


    public IAgentTool? Find(string name) {
        return byName.GetValueOrDefault(name);
    }


    public async Task<string> InvokeAsync(
        string name,
        JsonElement arguments,
        CancellationToken cancellationToken = default
    ) {
        IAgentTool? tool = Find(name);

        if (tool is null) {
            throw new UnknownAgentToolException(name);
        }

        return await tool.InvokeAsync(arguments, cancellationToken);
    }
}


public class UnknownAgentToolException(string name) : Exception($"No agent tool named '{name}'.") {

    public string name { get; } = name;
}
