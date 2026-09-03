using System.Text.Json;
using NektoTranslate.Common.Tools;
using NektoTranslate.Glossary.Enums;
using NektoTranslate.Glossary.Services;


namespace NektoTranslate.Glossary.Tools;


// Settles a term. Precedence is enforced by the service, not by the caller: what the book's own
// translation established outranks anything the model invents, and a hand edit outranks both. A
// tool call cannot talk its way past that ordering, which is the point - the rule has to hold no
// matter who is asking.
public class GlossaryRecordTool(IGlossaryService glossary) : IAgentTool {

    public string name => "glossary_record";

    public string description =>
        "Record how a source-language term is rendered in a novel's target language. "
        + "A rendering recovered from a translation the book already has outranks an invented one, "
        + "and a hand edit outranks both, so a weaker origin never overwrites a stronger one.";

    public string inputSchema => """
        {
          "type": "object",
          "properties": {
            "novelId": { "type": "integer", "description": "Novel the glossary belongs to." },
            "language": { "type": "string", "description": "Target language of the rendering." },
            "sourceTerm": { "type": "string", "description": "The term as spelled in the source language." },
            "targetTerm": { "type": "string", "description": "The rendering. Separate alternative forms with '/'." },
            "origin": {
              "type": "string",
              "enum": ["AiExtracted", "FromExistingTranslation", "Manual"],
              "description": "Where the rendering came from. Governs precedence."
            },
            "chapterId": { "type": "integer", "description": "Chapter this was settled from, if any." }
          },
          "required": ["novelId", "language", "sourceTerm", "targetTerm", "origin"],
          "additionalProperties": false
        }
        """;


    public async Task<string> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken = default) {
        long novelId = arguments.GetProperty("novelId").GetInt64();
        string language = arguments.GetProperty("language").GetString() ?? string.Empty;
        string sourceTerm = arguments.GetProperty("sourceTerm").GetString() ?? string.Empty;
        string targetTerm = arguments.GetProperty("targetTerm").GetString() ?? string.Empty;
        string originName = arguments.GetProperty("origin").GetString() ?? string.Empty;

        if (!Enum.TryParse(originName, out GlossaryEntryOrigin origin)) {
            return JsonSerializer.Serialize(new { recorded = false, reason = $"Unknown origin '{originName}'." });
        }

        long? chapterId = arguments.TryGetProperty("chapterId", out JsonElement chapterValue)
            ? chapterValue.GetInt64()
            : null;

        await glossary.RecordAsync(
            novelId,
            language,
            sourceTerm,
            targetTerm,
            origin,
            chapterId,
            cancellationToken
        );

        return JsonSerializer.Serialize(new { recorded = true, sourceTerm, targetTerm, origin = origin.ToString() });
    }
}
