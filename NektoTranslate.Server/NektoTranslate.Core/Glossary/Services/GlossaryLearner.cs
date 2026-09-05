using Microsoft.EntityFrameworkCore;
using NektoTranslate.Chapters.Contracts;
using NektoTranslate.Chapters.Entities;
using NektoTranslate.Chapters.Enums;
using NektoTranslate.Chapters.Services;
using NektoTranslate.Common.Data;
using NektoTranslate.Common.Models;
using NektoTranslate.Novels.Entities;
using NektoTranslate.Settings.Entities;
using NektoTranslate.Settings.Services;


namespace NektoTranslate.Glossary.Services;


// What a glossary-learning pass produced: how many chapters it read, how many of the candidate
// terms those chapters mentioned came back with a rendering recovered from the existing
// translation, how many stayed unresolved, and what the pass cost.
public sealed record LearnedGlossary(int chaptersRead, int renderingsLearned, int stillUnknown, double costUsd);


public interface IGlossaryLearner {

    Task<LearnedGlossary> LearnAsync(
        long novelId,
        string language,
        int fromChapterIndex,
        int toChapterIndex,
        CancellationToken cancellationToken = default
    );
}


// Reads the chapters of a range that carry both an original and a translation, and records how each
// term the original mentions was actually rendered.
//
// This is the extraction and reconciliation half of ChapterTranslator.TranslateAsync, run on its
// own against chapters a translate pass will never revisit: segment the source, list its candidate
// terms, and ask the glossary to find the rendering the existing translation already used for each
// one. Nothing here writes a translation or reads VoiceLearner's territory - it only grows the
// glossary from chapters whose human translation already answers the question a translate pass
// would otherwise have to guess at.
//
// A chapter is skipped once its glossaryState turns Analyzed, whether that happened here or in a
// later translate pass - re-reading it would spend money to confirm what is already settled.
public class GlossaryLearner(
    NektoDbContext database,
    IChapterSegmenter segmenter,
    ITermExtractor extractor,
    IGlossaryService glossary,
    ISettingsService settings
) : IGlossaryLearner {

    public async Task<LearnedGlossary> LearnAsync(
        long novelId,
        string language,
        int fromChapterIndex,
        int toChapterIndex,
        CancellationToken cancellationToken = default
    ) {
        Novel novel = await database.novels.FirstAsync(n => n.id == novelId, cancellationToken);

        List<Chapter> chapters = await database.chapters
            .Include(chapter => chapter.translations)
            .Where(chapter => chapter.novelId == novelId
                && chapter.index >= fromChapterIndex
                && chapter.index <= toChapterIndex)
            .OrderBy(chapter => chapter.index)
            .ToListAsync(cancellationToken);

        List<Chapter> qualifying = chapters
            .Where(chapter => Qualifies(chapter, language, fromChapterIndex, toChapterIndex))
            .ToList();

        if (qualifying.Count == 0) {
            return new LearnedGlossary(0, 0, 0, 0);
        }

        ApplicationSettings applicationSettings = await settings.GetAsync(cancellationToken);
        string glossaryModel = applicationSettings.glossaryModel;

        int renderingsLearned = 0;
        int stillUnknownCount = 0;
        double costUsd = 0;

        foreach (Chapter chapter in qualifying) {
            SegmentedChapter segmented = segmenter.Segment(chapter.sourceMarkdown!);
            List<string> sourceTexts = segmented.segments.Select(segment => segment.text).ToList();

            (IReadOnlyList<string> candidates, double extractionCost) = await extractor.ExtractCandidatesAsync(
                sourceTexts.Select(MarkdownText.Strip).ToList(),
                novel.sourceLanguage,
                glossaryModel,
                cancellationToken
            );

            costUsd += extractionCost;

            (IReadOnlyList<string> stillUnknown, double resolveCost) = await glossary.ReconcileWithExistingAsync(
                novelId,
                language,
                candidates,
                glossaryModel,
                cancellationToken
            );

            costUsd += resolveCost;

            renderingsLearned += candidates.Count - stillUnknown.Count;
            stillUnknownCount += stillUnknown.Count;

            chapter.glossaryState = ChapterGlossaryState.Analyzed;
            await database.SaveChangesAsync(cancellationToken);
        }

        return new LearnedGlossary(qualifying.Count, renderingsLearned, stillUnknownCount, costUsd);
    }


    // Whether one chapter belongs to a glossary-learning pass: it has an original to read, a
    // translation in the target language to compare it against, has not already given up its terms
    // to an earlier pass, and falls inside the range the caller asked for.
    //
    // Kept separate from the query above and tested on its own, because this is the one rule a
    // caller can get subtly wrong without any other test noticing - spending money re-reading a
    // chapter that is already settled, or on one with nothing on the other side to compare against.
    public static bool Qualifies(Chapter chapter, string language, int fromChapterIndex, int toChapterIndex) {
        return chapter.index >= fromChapterIndex
            && chapter.index <= toChapterIndex
            && chapter.sourceMarkdown != null
            && chapter.glossaryState == ChapterGlossaryState.NotAnalyzed
            && chapter.translations.Any(translation => translation.language == language);
    }
}
