using Microsoft.AspNetCore.SignalR;
using NektoTranslate.Chapters.Enums;
using NektoTranslate.Translation.Hubs;


namespace NektoTranslate.Translation.Services;


public class SignalRTranslationNotifier(IHubContext<TranslationHub> hub) : ITranslationNotifier {

    public Task JobStateChangedAsync(
        long novelId,
        long jobId,
        string state,
        int processed,
        int total,
        double costUsd
    ) {
        return Send(novelId, "JobStateChanged", new { jobId, state, processed, total, costUsd });
    }


    public Task ChapterStateChangedAsync(long novelId, long chapterId, ChapterTranslationState state) {
        return Send(novelId, "ChapterStateChanged", new { chapterId, state = state.ToString() });
    }


    public Task TranslationDeltaAsync(long novelId, long chapterId, string text) {
        return Send(novelId, "TranslationDelta", new { chapterId, text });
    }


    public Task ChapterTranslatedAsync(long novelId, long chapterId) {
        return Send(novelId, "ChapterTranslated", new { chapterId });
    }


    public Task GlossaryChangedAsync(long novelId, string sourceTerm, string targetTerm, string origin) {
        return Send(novelId, "GlossaryChanged", new { sourceTerm, targetTerm, origin });
    }


    public Task AgentMessageAsync(long novelId, string message) {
        return Send(novelId, "AgentMessage", new { message });
    }


    private Task Send(long novelId, string method, object payload) {
        return hub.Clients.Group(TranslationHub.GroupFor(novelId)).SendAsync(method, payload);
    }
}
