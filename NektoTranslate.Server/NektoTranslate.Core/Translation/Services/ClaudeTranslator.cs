using ClaudeCodeSdk;
using ClaudeCodeSdk.Types;
using NektoTranslate.Translation.Contracts;


namespace NektoTranslate.Translation.Services;


// Drives the Claude Code CLI as a subprocess through ClaudeCodeSdk, stripped down to a plain
// translation call.
//
// Claude Code normally sends its own system prompt, every built-in tool definition and the
// user's CLAUDE.md on every request. Measured on a single sentence that is ~29k input tokens
// and a chatty answer instead of a translation. Three flags remove it:
//
//   --safe-mode        drops CLAUDE.md, skills, plugins, hooks, MCP servers and custom agents,
//                      while leaving authentication working. This is the difference from
//                      --bare, which also refuses to read OAuth credentials and so breaks
//                      subscription login.
//   --system-prompt    replaces the Claude Code system prompt rather than appending to it.
//   --tools ""         sends no tool definitions at all.
//
// Same sentence with all three: ~358 input tokens, no commentary, roughly a thirtieth of the
// cost. The user's own Claude Code login still authenticates the call.
public class ClaudeTranslator : ITranslator {

    // The model's default reflex is to be helpful: to preface the translation, gloss the source
    // word by word, or answer a line of dialogue as though it were addressed to it. Naming each
    // of those refusals explicitly is what suppresses them; a general "output only the
    // translation" does not hold across runs.
    private const string SystemPromptTemplate =
        "You are a translation engine, not an assistant. The input is a fragment of a {0} light "
        + "novel. Your entire output is the {1} translation of that fragment.\n"
        + "Never write a preamble, a heading, a note, a gloss, a word-by-word breakdown, a "
        + "literal alternative, or a comment on the source. Never address the reader.\n"
        + "If the fragment is speech addressed to a character, translate it. Do not answer it.\n"
        + "Preserve honorifics as {1} transliterations, and preserve the speaker's register and "
        + "level of politeness. Use {1} quotation marks.";

    private readonly string model;


    public ClaudeTranslator(string model = "sonnet") {
        this.model = model;
    }


    public async Task<TranslationResult> TranslateAsync(
        TranslateTextRequest request,
        CancellationToken cancellationToken = default
    ) {
        ClaudeCodeOptions options = new ClaudeCodeOptions {
            Model = model,
            SystemPrompt = string.Format(SystemPromptTemplate, request.sourceLanguage, request.targetLanguage),
            MaxTurns = 1,
            ExtraArgs = new Dictionary<string, string?> {
                { "safe-mode", null },
                { "tools", "" },
                { "no-session-persistence", null }
            }
        };

        string prompt = $"Translate to {request.targetLanguage}:\n\n{request.sourceText}";
        List<string> blocks = [];
        string sessionId = string.Empty;
        double costUsd = 0;

        await foreach (IMessage message in ClaudeQuery.QueryAsync(prompt, options, null, cancellationToken)) {
            if (message is AssistantMessage assistant) {
                blocks.AddRange(assistant.Content.OfType<TextBlock>().Select(block => block.Text));
            }

            if (message is ResultMessage result) {
                sessionId = result.SessionId;
                costUsd = result.TotalCostUsd ?? 0;

                if (result.IsError) {
                    throw new InvalidOperationException($"Claude Code returned an error: {result.Result}");
                }
            }
        }

        return new TranslationResult(string.Join("\n", blocks), sessionId, costUsd);
    }
}
