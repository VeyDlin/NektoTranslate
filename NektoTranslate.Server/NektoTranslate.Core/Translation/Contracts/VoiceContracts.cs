namespace NektoTranslate.Translation.Contracts;


// What a voice-learning pass produced: the profile it wrote, how many terms it read off the sample
// chapters, and what the pass cost, so the caller can report a run without a second read of the row.
public sealed record LearnedVoice(
    long profileId,
    string summary,
    int termCount,
    double costUsd
);


// Reads a span of chapters that already carry a human translation and writes a VoiceProfile
// describing how that translator renders the book, so later translate and repair passes can be
// told to imitate it instead of falling back to a generic style.
public interface IVoiceLearner {

    Task<LearnedVoice> LearnAsync(
        long novelId,
        string language,
        int fromChapterIndex,
        int toChapterIndex,
        CancellationToken cancellationToken = default
    );
}


// What a repair pass produced: which translation it wrote or replaced, how many blocks it touched,
// and what the pass cost.
public sealed record RepairedChapter(
    long translationId,
    int blocks,
    double costUsd
);


// Fixes one chapter of an imported translation in place - the source is missing or unreliable, so
// this corrects the existing rendering rather than retranslating from an original.
public interface IChapterRepairer {

    Task<RepairedChapter> RepairAsync(
        long chapterId,
        CancellationToken cancellationToken = default
    );
}
