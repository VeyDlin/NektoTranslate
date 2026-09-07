using NektoTranslate.Chapters.Enums;
using NektoTranslate.Jobs.Contracts;
using NektoTranslate.Jobs.Enums;
using NektoTranslate.Parsing.Enums;


namespace NektoTranslate.Translation.Services;


// Everything the interface can show while a run is under way. The chapter events exist so a reader
// can start on chapter twelve while thirteen is still being written - publishing only at the end of
// a run would make a thousand-chapter job feel like it does nothing for hours.
public interface ITranslationNotifier {

    Task JobStateChangedAsync(
        long novelId,
        long jobId,
        string state,
        int processed,
        int total,
        double costUsd,
        string? currentStep,
        int? stepIndex,
        int? stepCount
    );


    Task ChapterStateChangedAsync(long novelId, long chapterId, ChapterTranslationState state);


    // Prose as it is written, markers already stripped. Lets a reader watch a chapter appear
    // instead of staring at a spinner for the minute it takes.
    Task TranslationDeltaAsync(long novelId, long chapterId, string text);


    Task ChapterTranslatedAsync(long novelId, long chapterId);


    // Which version is current changed with nothing else about the chapter changing - not a
    // translation, not its state. Kept apart from ChapterTranslatedAsync because that event also
    // tells the chapter list the chapter is now Translated and its glossary Analyzed, neither of
    // which is true here: a chapter already sitting at Failed stays Failed when an older version is
    // pinned back to current.
    Task ChapterCurrentVersionChangedAsync(long novelId, long chapterId);


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


    // Reading a site's contents is a page load behind a queue, so it is slow enough that a screen
    // waiting on it has to be told when it lands - including a screen opened after the read began,
    // which is the whole reason the read is persisted rather than held in a request.
    Task ListingStateChangedAsync(long novelId, ImportKind kind, ListingState state, int entryCount);
}
