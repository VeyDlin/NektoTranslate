using System.Text.Json;
using System.Text.Json.Serialization;


namespace NektoTranslate.Chat.Services;


// What the agent decided to do this turn: answer, or run a tool.
//
// Parsing is deliberately forgiving. A model asked for bare JSON will now and then wrap it in a code
// fence or put a sentence in front of it, and refusing the turn over that would make the chat feel
// broken for a formatting habit. Anything that cannot be read as an action is treated as a plain
// reply, which is the harmless interpretation.
public sealed record ChatAction {

    [JsonPropertyName("action")]
    public string? action { get; init; }

    [JsonPropertyName("name")]
    public string? name { get; init; }

    [JsonPropertyName("text")]
    public string? text { get; init; }

    [JsonPropertyName("arguments")]
    public JsonElement arguments { get; init; }


    public static ChatAction? Parse(string reply) {
        string candidate = Unfence(reply).Trim();

        int start = candidate.IndexOf('{');
        int end = candidate.LastIndexOf('}');

        if (start < 0 || end <= start) {
            return null;
        }

        try {
            return JsonSerializer.Deserialize<ChatAction>(candidate[start..(end + 1)]);
        } catch (JsonException) {
            return null;
        }
    }


    private static string Unfence(string reply) {
        string trimmed = reply.Trim();

        if (!trimmed.StartsWith("```", StringComparison.Ordinal)) {
            return trimmed;
        }

        int firstBreak = trimmed.IndexOf('\n');
        int closing = trimmed.LastIndexOf("```", StringComparison.Ordinal);

        if (firstBreak < 0 || closing <= firstBreak) {
            return trimmed;
        }

        return trimmed[(firstBreak + 1)..closing];
    }
}
