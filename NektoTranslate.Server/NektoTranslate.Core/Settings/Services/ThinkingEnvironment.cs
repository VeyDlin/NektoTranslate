namespace NektoTranslate.Settings.Services;


// Turns a thinking-token budget into the environment variable the Claude Code CLI actually reads.
//
// ClaudeCodeOptions.MaxThinkingTokens exists in the vendored SDK but ClaudeProcess never forwards it
// to the CLI process - only ClaudeCodeOptions.EnvironmentVariables reaches it, and MAX_THINKING_TOKENS
// is the variable the CLI itself reads from the environment. Kept apart from the callers that build a
// full ClaudeCodeOptions so the one rule that matters - a translate, repair or proofread call thinks,
// a glossary model's mechanical call (term extraction, rendering lookup, name alignment, voice
// learning) never does - can be asserted directly rather than only by reading a live prompt.
public static class ThinkingEnvironment {

    public const string ThinkingTokensVariable = "MAX_THINKING_TOKENS";


    // Null rather than an empty dictionary when there is nothing to set - ClaudeCodeOptions leaves
    // EnvironmentVariables null by default, and returning null for a budget of zero keeps "thinking
    // turned off" indistinguishable from a call that never knew this setting existed.
    public static Dictionary<string, string?>? BuildEnvironmentVariables(int thinkingTokens) {
        if (thinkingTokens <= 0) {
            return null;
        }

        return new Dictionary<string, string?> {
            { ThinkingTokensVariable, thinkingTokens.ToString() }
        };
    }
}
