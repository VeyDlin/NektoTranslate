using NektoTranslate.Chapters.Enums;


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
}
