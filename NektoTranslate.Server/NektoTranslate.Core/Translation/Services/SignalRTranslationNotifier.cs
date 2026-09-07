using Microsoft.AspNetCore.SignalR;
using NektoTranslate.Chapters.Enums;
using NektoTranslate.Jobs.Contracts;
using NektoTranslate.Jobs.Enums;
using NektoTranslate.Parsing.Enums;
using NektoTranslate.Translation.Hubs;


namespace NektoTranslate.Translation.Services;


public class SignalRTranslationNotifier(IHubContext<TranslationHub> hub) : ITranslationNotifier {

    public Task JobStateChangedAsync(
        long novelId,
        long jobId,
        string state,
        int processed,
        int total,
        double costUsd,
        string? currentStep,
        int? stepIndex,
        int? stepCount
    ) {
        return Send(novelId, "JobStateChanged", new {
            jobId, state, processed, total, costUsd, currentStep, stepIndex, stepCount
        });
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


    public Task ChapterCurrentVersionChangedAsync(long novelId, long chapterId) {
        return Send(novelId, "ChapterCurrentVersionChanged", new { chapterId });
    }


    public Task GlossaryChangedAsync(long novelId, string sourceTerm, string targetTerm, string origin) {
        return Send(novelId, "GlossaryChanged", new { sourceTerm, targetTerm, origin });
    }


    public Task AgentMessageAsync(long novelId, string message) {
        return Send(novelId, "AgentMessage", new { message });
    }


    // Enums go out as names here, as they do on every other event: the hub's serializer does not
    // share the controllers' converter, and a client decoding numbers from one channel and names
    // from the other is a bug waiting for a reordered enum.
    public Task ImportStateChangedAsync(
        long novelId,
        long jobId,
        ImportKind kind,
        JobState state,
        int processed,
        int total,
        string? currentTitle
    ) {
        return Send(novelId, "ImportStateChanged", new {
            jobId,
            kind = kind.ToString(),
            state = state.ToString(),
            processed,
            total,
            currentTitle
        });
    }


    public Task ImportItemFinishedAsync(long novelId, long jobId, ImportJobItemView item) {
        return Send(novelId, "ImportItemFinished", new {
            jobId,
            item.position,
            item.sourceUrl,
            item.title,
            item.chapterIndex,
            item.chapterId,
            state = item.state.ToString(),
            item.status,
            item.finishedAt
        });
    }


    public Task ListingStateChangedAsync(long novelId, ImportKind kind, ListingState state, int entryCount) {
        return Send(novelId, "ListingStateChanged", new {
            kind = kind.ToString(),
            state = state.ToString(),
            entryCount
        });
    }


    private Task Send(long novelId, string method, object payload) {
        return hub.Clients.Group(TranslationHub.GroupFor(novelId)).SendAsync(method, payload);
    }
}
