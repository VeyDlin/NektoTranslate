using System.Text.Json;
using ClaudeCodeSdk;
using ClaudeCodeSdk.Types;
using NektoTranslate.Common.Contracts;
using NektoTranslate.Common.Models;
using NektoTranslate.Settings.Contracts;
using NektoTranslate.Settings.Entities;


namespace NektoTranslate.Settings.Services;


public interface IModelCatalog {

    IReadOnlyList<ModelOption> List();


    Task<ModelProbeResult> ProbeAsync(string model, CancellationToken cancellationToken = default);


    Task<LocalModelList> ListLocalAsync(CancellationToken cancellationToken = default);
}


// Supplies the model choices the interface offers, so a model is picked from a list rather than
// typed. A typed model name is a silent failure waiting to happen: a typo is only discovered when a
// translation run dies partway through.
//
// The two sides of the application answer this question very differently, and the asymmetry is not
// an oversight:
//
//   local models   really are enumerable. Every OpenAI-compatible server - Ollama, LM Studio,
//                  llama.cpp, vLLM - answers GET /v1/models with what it has, so that list is
//                  fetched live and is exactly right.
//
//   Claude models  are not. The CLI has no subcommand that prints its catalogue; --model simply
//                  accepts a name and rejects an unknown one with "isn't described by this
//                  version's model catalog". The catalogue exists inside the executable, but
//                  reading it out of a binary is not a mechanism - it would break without warning
//                  on any release that changed the packaging.
//
// So the Claude side is a configured list that can be *verified* rather than a guessed one. The
// list itself is aliases, which do not go stale, and Probe answers the question the list cannot:
// whether this particular subscription can actually run a given model.
public class ModelCatalog(
    EngineOptions options,
    ISettingsService settings,
    IHttpClientFactory httpClientFactory
) : IModelCatalog {

    public IReadOnlyList<ModelOption> List() {
        return options.models;
    }


    // Asks the CLI the only way it can be asked: by using the model.
    //
    // A rejected name costs nothing - the CLI refuses before reaching the API. A valid one costs a
    // single trivial turn, which is why this is a deliberate action in the interface and not
    // something run on every page load.
    public async Task<ModelProbeResult> ProbeAsync(
        string model,
        CancellationToken cancellationToken = default
    ) {
        ClaudeCodeOptions probe = new ClaudeCodeOptions {
            Model = model,
            SystemPrompt = "Reply with the single character: 1",
            MaxTurns = 1,
            ExtraArgs = new Dictionary<string, string?> {
                { "safe-mode", null },
                { "tools", "" },
                { "no-session-persistence", null }
            }
        };

        try {
            await foreach (IMessage message in ClaudeQuery.QueryAsync(".", probe, null, cancellationToken)) {
                if (message is ResultMessage result && result.IsError) {
                    return new ModelProbeResult(model, false, Rejected(result.Result));
                }
            }

            return new ModelProbeResult(model, true, null);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception failure) {
            return new ModelProbeResult(model, false, Rejected(failure.Message));
        }
    }


    // The genuine enumeration. Returns unreachable rather than throwing, because "no local server
    // is running" is an ordinary state for this application, not an error.
    public async Task<LocalModelList> ListLocalAsync(CancellationToken cancellationToken = default) {
        ApplicationSettings current = await settings.GetAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(current.localModelEndpoint)) {
            return new LocalModelList(false, [], Statuses.LocalModelNotConfigured);
        }

        try {
            HttpClient client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            string url = $"{current.localModelEndpoint.TrimEnd('/')}/models";

            if (!string.IsNullOrWhiteSpace(current.localModelApiKey)) {
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {current.localModelApiKey}");
            }

            using HttpResponseMessage response = await client.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode) {
                return new LocalModelList(
                    false,
                    [],
                    Statuses.LocalModelBadResponse.With(("url", url), ("status", (int)response.StatusCode))
                );
            }

            string body = await response.Content.ReadAsStringAsync(cancellationToken);

            return new LocalModelList(true, ReadModelIds(body), null);
        } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            return new LocalModelList(false, [], Statuses.LocalModelTimedOut);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception failure) {
            return new LocalModelList(
                false,
                [],
                Statuses.LocalModelListFailed.With(("reason", Shorten(failure.Message) ?? failure.GetType().Name))
            );
        }
    }


    // { "object": "list", "data": [ { "id": "qwen2.5:7b", ... }, ... ] }
    private static List<string> ReadModelIds(string body) {
        List<string> ids = [];

        using JsonDocument document = JsonDocument.Parse(body);

        if (!document.RootElement.TryGetProperty("data", out JsonElement data)) {
            return ids;
        }

        foreach (JsonElement entry in data.EnumerateArray()) {
            if (entry.TryGetProperty("id", out JsonElement id) && id.GetString() is string value) {
                ids.Add(value);
            }
        }

        ids.Sort(StringComparer.OrdinalIgnoreCase);

        return ids;
    }


    // The CLI does sometimes refuse without saying why - an empty result with the error flag set -
    // and that gets its own status rather than a sentence with nothing after the colon.
    private static Status Rejected(string? message) {
        string? reason = Shorten(message);

        return reason is null
            ? Statuses.ModelRejectedWithoutReason
            : Statuses.ModelRejected.With(("reason", reason));
    }


    // The first line only. A CLI failure arrives with its whole trace attached, and the line that
    // names the problem is the first one; the rest is noise to a person choosing a model.
    private static string? Shorten(string? message) {
        if (string.IsNullOrWhiteSpace(message)) {
            return null;
        }

        string line = message.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? message;

        return line.Length <= 300 ? line : line[..300];
    }
}
