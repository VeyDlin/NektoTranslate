using NektoTranslate.Chapters.Enums;
using NektoTranslate.Jobs.Contracts;
using NektoTranslate.Jobs.Enums;


namespace NektoTranslate.Translation.Services;


// Everything the interface can show while a run is under way. The chapter events exist so a reader
// can start on chapter twelve while thirteen is still being written - publishing only at the end of
// a run would make a thousand-chapter job feel like it does nothing for hours.
public interface ITranslationNotifier {

    Task JobStateChangedAsync(long novelId, long jobId, string state, int processed, int total, double costUsd);


    Task ChapterStateChangedAsync(long novelId, long chapterId, ChapterTranslationState state);


    // Prose as it is written, markers already stripped. Lets a reader watch a chapter appear
    // instead of staring at a spinner for the minute it takes.
    Task TranslationDeltaAsync(long novelId, long chapterId, string text);


    Task ChapterTranslatedAsync(long novelId, long chapterId);


    Task GlossaryChangedAsync(long novelId, string sourceTerm, string targetTerm, string origin);


    Task AgentMessageAsync(long novelId, string message);


    // Imports report the same way runs do, and for the same reason: a page opened while forty
    // chapters are being fetched has to show the fetch, and a strip that only updates at the end
    // is a strip that looks frozen.
    Task ImportStateChangedAsync(
        long novelId,
        long jobId,
        ImportKind kind,
        JobState state,
        int processed,
        int total,
        string? currentTitle
    );


    // One chapter's outcome, the moment it is known. The list of these is the report.
    Task ImportItemFinishedAsync(long novelId, long jobId, ImportJobItemView item);
}
