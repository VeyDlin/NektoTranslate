using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NektoTranslate.Common.Data;
using NektoTranslate.Common.Tools;
using NektoTranslate.Glossary.Entities;


namespace NektoTranslate.Glossary.Tools;


// Answers "what is this term already called in this book". The first thing an agent should reach
// for before writing a name, and the cheapest: a single indexed read, no model call.
public class GlossaryLookupTool(NektoDbContext database) : IAgentTool {

    public string name => "glossary_lookup";

    public string description =>
        "Look up the established rendering of a term in a novel's glossary. "
        + "Returns the rendering, where it came from, and any notes on gender, register or form of "
        + "address. Returns an empty result when the term has not been settled yet.";

    public string inputSchema => """
        {
          "type": "object",
          "properties": {
            "novelId": { "type": "integer", "description": "Novel the glossary belongs to." },
            "language": { "type": "string", "description": "Target language of the rendering." },
            "sourceTerm": { "type": "string", "description": "The term as spelled in the source language." }
          },
          "required": ["novelId", "language", "sourceTerm"],
          "additionalProperties": false
        }
        """;


    public async Task<string> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken = default) {
        long novelId = arguments.GetProperty("novelId").GetInt64();
        string language = arguments.GetProperty("language").GetString() ?? string.Empty;
        string sourceTerm = arguments.GetProperty("sourceTerm").GetString() ?? string.Empty;

        GlossaryEntry? entry = await database.glossaryEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.novelId == novelId
                    && candidate.language == language
                    && candidate.sourceTerm == sourceTerm,
                cancellationToken
            );

        if (entry is null) {
            return JsonSerializer.Serialize(new { found = false, sourceTerm });
        }

        return JsonSerializer.Serialize(new {
            found = true,
            entry.sourceTerm,
            entry.targetTerm,
            category = entry.category.ToString(),
            origin = entry.origin.ToString(),
            entry.notes,
            entry.aliases,
            entry.needsReview
        });
    }
}
