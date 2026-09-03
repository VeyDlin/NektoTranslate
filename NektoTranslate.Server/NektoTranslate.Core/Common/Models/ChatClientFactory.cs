using System.ClientModel;
using Microsoft.Extensions.AI;
using NektoTranslate.Settings.Entities;
using NektoTranslate.Settings.Services;
using OpenAI;


namespace NektoTranslate.Common.Models;


public interface IChatClientFactory {

    // Null rather than an exception when no local model is configured: running without one is the
    // ordinary case, not a fault. A single call answers both "is there one" and "give me it", which
    // avoids the window where availability is checked and the setting changes before it is used.
    Task<IChatClient?> CreateAsync(CancellationToken cancellationToken = default);
}


// Builds a chat client for a locally hosted model.
//
// Goes through the OpenAI-compatible surface rather than any one server's own API. Ollama, LM
// Studio, llama.cpp and vLLM all expose it, so a user who switches runner changes a URL rather than
// waiting for the application to add support - and the same client reaches a hosted aggregator if
// they would rather not run anything.
//
// Reads the endpoint from the database rather than configuration, because this is a setting the
// user changes from the interface: someone who starts Ollama should be able to point the
// application at it without editing a file and restarting.
public class ChatClientFactory(ISettingsService settings) : IChatClientFactory {

    public async Task<IChatClient?> CreateAsync(CancellationToken cancellationToken = default) {
        ApplicationSettings current = await settings.GetAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(current.localModelEndpoint)) {
            return null;
        }

        OpenAIClient client = new OpenAIClient(
            new ApiKeyCredential(current.localModelApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(current.localModelEndpoint) }
        );

        return client.GetChatClient(current.localModelName).AsIChatClient();
    }
}
