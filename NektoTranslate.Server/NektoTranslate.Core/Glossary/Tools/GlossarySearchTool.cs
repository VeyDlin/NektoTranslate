using System.Text.Json;
using NektoTranslate.Common.Tools;
using NektoTranslate.Glossary.Contracts;
using NektoTranslate.Glossary.Services;


namespace NektoTranslate.Glossary.Tools;


// Finds where a term occurs on the source side, down to the paragraph.
//
// Searches the source rather than the translation, because the source is the stable side - the
// rendering is exactly what drifts between chapters and translators. Paragraph granularity keeps
// any follow-up cheap: only that paragraph and its counterpart need to be read, never a chapter.
public class GlossarySearchTool(ITermLocator locator) : IAgentTool {

    private const int DefaultLimit = 5;


    public string name => "glossary_search";

    public string description =>
        "Find the chapters and paragraphs where a source-language term occurs in a novel. "
        + "Use it to see how a name was handled earlier before deciding how to render it.";

    public string inputSchema => """
        {
          "type": "object",
          "properties": {
            "novelId": { "type": "integer", "description": "Novel to search." },
            "term": { "type": "string", "description": "The term as spelled in the source language." },
            "limit": { "type": "integer", "description": "Maximum chapters to report. Defaults to 5." }
          },
          "required": ["novelId", "term"],
          "additionalProperties": false
        }
        """;


    public async Task<string> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken = default) {
        long novelId = arguments.GetProperty("novelId").GetInt64();
        string term = arguments.GetProperty("term").GetString() ?? string.Empty;

        int limit = arguments.TryGetProperty("limit", out JsonElement limitValue)
            ? limitValue.GetInt32()
            : DefaultLimit;

        IReadOnlyList<TermOccurrence> occurrences = await locator.FindAsync(
            novelId,
            term,
            limit,
            cancellationToken
        );

        return JsonSerializer.Serialize(new {
            term,
            count = occurrences.Count,
            occurrences = occurrences.Select(occurrence => new {
                occurrence.chapterId,
                occurrence.chapterIndex,
                occurrence.segmentIndex,
                occurrence.segmentText
            })
        });
    }
}
