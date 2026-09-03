using System.Text.Json;
using NektoTranslate.Common.Tools;
using Xunit;


namespace NektoTranslate.Tests.Common;


public class AgentToolRegistryTests {

    [Fact]
    public void ToolsAreReachableByName() {
        AgentToolRegistry registry = new AgentToolRegistry([new EchoTool("first"), new EchoTool("second")]);

        Assert.Equal("first", registry.Find("first")?.name);
        Assert.Equal(2, registry.All().Count);
    }


    [Fact]
    public void AnUnknownNameIsRefusedRatherThanIgnored() {
        AgentToolRegistry registry = new AgentToolRegistry([new EchoTool("first")]);

        UnknownAgentToolException failure = Assert.Throws<UnknownAgentToolException>(
            () => registry.Find("missing") ?? throw new UnknownAgentToolException("missing")
        );

        Assert.Equal("missing", failure.name);
    }


    [Fact]
    public async Task InvokingReachesTheTool() {
        AgentToolRegistry registry = new AgentToolRegistry([new EchoTool("first")]);
        JsonElement arguments = JsonDocument.Parse("""{"value":"hello"}""").RootElement;

        string result = await registry.InvokeAsync("first", arguments, TestContext());

        Assert.Equal("hello", result);
    }


    [Fact]
    public async Task InvokingAnUnknownToolThrows() {
        AgentToolRegistry registry = new AgentToolRegistry([]);
        JsonElement arguments = JsonDocument.Parse("{}").RootElement;

        await Assert.ThrowsAsync<UnknownAgentToolException>(
            () => registry.InvokeAsync("missing", arguments, TestContext())
        );
    }


    [Fact]
    public void EverySchemaIsValidJsonSchema() {
        foreach (IAgentTool tool in new IAgentTool[] { new EchoTool("first") }) {
            JsonElement schema = JsonDocument.Parse(tool.inputSchema).RootElement;

            Assert.Equal("object", schema.GetProperty("type").GetString());
        }
    }


    private static CancellationToken TestContext() {
        return CancellationToken.None;
    }


    private sealed class EchoTool(string toolName) : IAgentTool {

        public string name => toolName;

        public string description => "Echoes its argument back.";

        public string inputSchema => """
            {"type":"object","properties":{"value":{"type":"string"}},"required":["value"]}
            """;


        public Task<string> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken = default) {
            return Task.FromResult(arguments.GetProperty("value").GetString() ?? string.Empty);
        }
    }
}
