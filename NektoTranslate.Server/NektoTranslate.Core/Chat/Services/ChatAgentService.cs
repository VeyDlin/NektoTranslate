using System.Text;
using System.Text.Json;
using ClaudeCodeSdk;
using ClaudeCodeSdk.Types;
using Microsoft.EntityFrameworkCore;
using NektoTranslate.Chat.Entities;
using NektoTranslate.Chat.Enums;
using NektoTranslate.Common.Data;
using NektoTranslate.Common.Models;
using NektoTranslate.Common.Tools;
using NektoTranslate.Novels.Entities;
using NektoTranslate.Settings.Services;
using NektoTranslate.Translation.Services;


namespace NektoTranslate.Chat.Services;


public interface IChatAgentService {

    Task<IReadOnlyList<ChatMessage>> SendAsync(
        long novelId,
        string text,
        CancellationToken cancellationToken = default
    );
}


// The conversation where the user corrects the translation: "that name is wrong, call him X from
// now on". Corrections are contextual and must outlive the message, so the agent acts through the
// tool registry rather than only replying - a correction that did not reach the glossary would be
// forgotten by the next chapter.
//
// The tools are described in the prompt and executed here, rather than exposed to the model over
// MCP. Measured, MCP costs about a hundred times more per request, because tool definitions cannot
// reach Claude Code without dragging its entire agent prompt back in. Describing three tools in
// prose and parsing one JSON line back costs nothing and reaches the same registry.
public class ChatAgentService(
    NektoDbContext database,
    IAgentToolRegistry tools,
    ITranslationNotifier notifier,
    ISettingsService settings,
    EngineOptions options,
    string model = "sonnet"
) : IChatAgentService {

    private readonly int historyTurns = options.chat.historyTurns;


    public async Task<IReadOnlyList<ChatMessage>> SendAsync(
        long novelId,
        string text,
        CancellationToken cancellationToken = default
    ) {
        Novel novel = await database.novels.FirstAsync(candidate => candidate.id == novelId, cancellationToken);

        List<ChatMessage> written = [];
        ChatMessage question = new ChatMessage { novelId = novelId, role = ChatRole.User, text = text };

        database.chatMessages.Add(question);
        await database.SaveChangesAsync(cancellationToken);
        written.Add(question);

        List<string> transcript = await RecentTranscriptAsync(novelId, cancellationToken);
        string systemPrompt = BuildSystemPrompt(novel);
        double costUsd = 0;

        // A ceiling on what one message can cost. Enough rounds to look something up, act on it and
        // confirm; short enough that a confused agent cannot spend the afternoon on it.
        int maxRounds = (await settings.GetAsync(cancellationToken)).chatMaxRounds;

        for (int round = 0; round < maxRounds; round++) {
            (string reply, double roundCost) = await AskAsync(
                systemPrompt,
                string.Join("\n", transcript),
                cancellationToken
            );

            costUsd += roundCost;

            ChatAction? action = ChatAction.Parse(reply);

            if (action is null || action.action == "reply") {
                ChatMessage answer = new ChatMessage {
                    novelId = novelId,
                    role = ChatRole.Agent,
                    text = action?.text ?? reply.Trim(),
                    costUsd = costUsd
                };

                database.chatMessages.Add(answer);
                await database.SaveChangesAsync(cancellationToken);
                written.Add(answer);

                await notifier.AgentMessageAsync(novelId, answer.text);

                return written;
            }

            string result = await RunToolAsync(action, novelId, cancellationToken);

            ChatMessage note = new ChatMessage {
                novelId = novelId,
                role = ChatRole.Tool,
                text = $"{action.name}: {result}"
            };

            database.chatMessages.Add(note);
            await database.SaveChangesAsync(cancellationToken);
            written.Add(note);

            transcript.Add($"TOOL {action.name} RESULT: {result}");
        }

        // Out of rounds. Saying so is better than silence: the user can see the agent got stuck
        // rather than wondering whether the message was delivered.
        ChatMessage exhausted = new ChatMessage {
            novelId = novelId,
            role = ChatRole.Agent,
            text = "I could not finish that within the allowed number of steps.",
            costUsd = costUsd
        };

        database.chatMessages.Add(exhausted);
        await database.SaveChangesAsync(cancellationToken);
        written.Add(exhausted);

        return written;
    }


    private async Task<string> RunToolAsync(ChatAction action, long novelId, CancellationToken cancellationToken) {
        try {
            JsonElement arguments = WithNovelId(action.arguments, novelId);

            return await tools.InvokeAsync(action.name ?? string.Empty, arguments, cancellationToken);
        } catch (Exception failure) {
            // Handed back to the agent rather than thrown: it can often recover by calling something
            // else, and a stack trace in the user's chat helps nobody.
            return JsonSerializer.Serialize(new { error = failure.Message });
        }
    }


    // The novel is context the user should not have to repeat and the agent should not be able to
    // get wrong, so it is filled in here rather than trusted from the model's arguments.
    private static JsonElement WithNovelId(JsonElement arguments, long novelId) {
        Dictionary<string, object?> merged = [];

        if (arguments.ValueKind == JsonValueKind.Object) {
            foreach (JsonProperty property in arguments.EnumerateObject()) {
                merged[property.Name] = property.Value.Clone();
            }
        }

        merged["novelId"] = novelId;

        return JsonSerializer.SerializeToElement(merged);
    }


    private async Task<List<string>> RecentTranscriptAsync(long novelId, CancellationToken cancellationToken) {
        List<ChatMessage> history = await database.chatMessages
            .AsNoTracking()
            .Where(message => message.novelId == novelId)
            .OrderByDescending(message => message.id)
            .Take(historyTurns)
            .ToListAsync(cancellationToken);

        history.Reverse();

        return history
            .Select(message => $"{message.role.ToString().ToUpperInvariant()}: {message.text}")
            .ToList();
    }


    private string BuildSystemPrompt(Novel novel) {
        StringBuilder prompt = new StringBuilder();

        prompt.AppendLine(
            $"You maintain the translation of a {novel.sourceLanguage} novel into {novel.targetLanguage}, "
            + $"titled \"{novel.title}\". The user corrects and questions your work."
        );
        prompt.AppendLine(
            "Act on corrections rather than only agreeing with them. A name the user rewrites must be "
            + "recorded, or it will be forgotten by the next chapter."
        );
        prompt.AppendLine();
        prompt.AppendLine("Reply with a single JSON object and nothing else, in one of two shapes:");
        prompt.AppendLine("""  {"action":"reply","text":"what you want to say"}""");
        prompt.AppendLine("""  {"action":"tool","name":"<tool name>","arguments":{ ... }}""");
        prompt.AppendLine(
            "Use a tool when you need to look something up or change something; reply when you are "
            + "done. The novelId is supplied for you - never invent one."
        );
        prompt.AppendLine();
        prompt.AppendLine("TOOLS");

        foreach (IAgentTool tool in tools.All()) {
            prompt.Append("- ").Append(tool.name).Append(": ").AppendLine(tool.description);
            prompt.Append("  arguments: ").AppendLine(tool.inputSchema.ReplaceLineEndings(" "));
        }

        if (!string.IsNullOrWhiteSpace(novel.styleGuide)) {
            prompt.AppendLine();
            prompt.AppendLine("STYLE GUIDE IN FORCE");
            prompt.AppendLine(novel.styleGuide);
        }

        return prompt.ToString();
    }


    private async Task<(string reply, double costUsd)> AskAsync(
        string systemPrompt,
        string prompt,
        CancellationToken cancellationToken
    ) {
        ClaudeCodeOptions options = new ClaudeCodeOptions {
            Model = model,
            SystemPrompt = systemPrompt,
            MaxTurns = 1,
            ExtraArgs = new Dictionary<string, string?> {
                { "safe-mode", null },
                { "tools", "" },
                { "no-session-persistence", null }
            }
        };

        StringBuilder reply = new StringBuilder();
        double costUsd = 0;

        await foreach (IMessage message in ClaudeQuery.QueryAsync(prompt, options, null, cancellationToken)) {
            if (message is AssistantMessage assistant) {
                foreach (TextBlock block in assistant.Content.OfType<TextBlock>()) {
                    reply.Append(block.Text);
                }
            }

            if (message is ResultMessage result) {
                costUsd += result.TotalCostUsd ?? 0;

                if (result.IsError) {
                    throw new InvalidOperationException($"Claude Code returned an error: {result.Result}");
                }
            }
        }

        return (reply.ToString(), costUsd);
    }
}
