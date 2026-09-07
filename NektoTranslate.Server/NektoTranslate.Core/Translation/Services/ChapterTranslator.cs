using System.Text;
using Microsoft.EntityFrameworkCore;
using NektoTranslate.Chapters.Contracts;
using NektoTranslate.Chapters.Entities;
using NektoTranslate.Chapters.Enums;
using NektoTranslate.Chapters.Services;
using NektoTranslate.Common.Data;
using NektoTranslate.Common.Models;
using NektoTranslate.Glossary.Enums;
using NektoTranslate.Glossary.Services;
using NektoTranslate.Jobs.Contracts;
using NektoTranslate.Novels.Entities;
using NektoTranslate.Settings.Entities;
using NektoTranslate.Settings.Services;
using NektoTranslate.Translation.Checks;
using NektoTranslate.Translation.Contracts;
using NektoTranslate.Translation.Entities;
using NektoTranslate.Translation.Enums;


namespace NektoTranslate.Translation.Services;


public interface IChapterTranslator {

    Task<ChapterTranslationSummary> TranslateAsync(
        long chapterId,
        IProgress<RunStep>? progress = null,
        CancellationToken cancellationToken = default
    );
}


// Translates one chapter and leaves the glossary a little larger than it found it.
//
// The order matters. Terms are resolved against any translation the book already has *before* the
// chapter is translated, so established names reach the model as instructions rather than being
// corrected afterwards. Whatever could not be resolved is settled from our own output once the
// chapter is done, and marked for review, because that rendering is the model's invention rather
// than something the reader has already met.
//
// Nothing here reads the book. Context is a short window of recent chapters, the glossary is
// filtered to the terms this chapter uses, and unknown terms cost one indexed lookup each.
public class ChapterTranslator(
    NektoDbContext database,
    IChapterSegmenter segmenter,
    ISegmentTranslator translator,
    ITermExtractor extractor,
    IGlossaryService glossary,
    ITranslationCheckRunner checks,
    ITranslationNotifier notifier,
    ISettingsService settings,
    EngineOptions engine
) : IChapterTranslator {

    public async Task<ChapterTranslationSummary> TranslateAsync(
        long chapterId,
        IProgress<RunStep>? progress = null,
        CancellationToken cancellationToken = default
    ) {
        Chapter chapter = await database.chapters
            .Include(c => c.novel)
            .FirstAsync(c => c.id == chapterId, cancellationToken);

        Novel novel = chapter.novel!;
        string language = novel.targetLanguage;

        // Nothing to translate from. A run never scopes these chapters, so reaching here means a
        // single chapter was asked for by id — and saying so plainly is better than segmenting an
        // empty string and calling the result a translation.
        if (chapter.sourceMarkdown is null) {
            throw new InvalidOperationException(
                $"Chapter {chapterId} has no original text. It can be edited or repaired, "
                + "but not translated."
            );
        }

        SegmentedChapter segmented = segmenter.Segment(chapter.sourceMarkdown);

        // Stored beside the Markdown at import, so it is only ever absent for a chapter whose
        // Markdown is absent too - and that case was refused above. Derived again rather than
        // asserted, so a chapter that somehow has the one without the other is still translated
        // from what it has.
        string sourcePlainText = chapter.sourcePlainText ?? segmented.PlainText();

        // A chapter with text that yields no segments is a markup shape the pipeline failed to
        // understand, not an empty chapter. Marking it translated would hand the reader a blank page
        // with a tick beside it, and the loss would only surface once the source was long gone.
        if (segmented.segments.Count == 0) {
            if (sourcePlainText.Trim().Length > 0) {
                throw new InvalidOperationException(
                    $"Chapter {chapterId} has text but produced no translatable segments; "
                    + "its markup was not recognised."
                );
            }

            chapter.translationState = ChapterTranslationState.Translated;
            await database.SaveChangesAsync(cancellationToken);

            return new ChapterTranslationSummary(chapterId, 0, 0, [], []);
        }

        List<string> sourceTexts = segmented.segments.Select(segment => segment.text).ToList();

        chapter.translationState = ChapterTranslationState.Running;
        await database.SaveChangesAsync(cancellationToken);

        ApplicationSettings applicationSettings = await settings.GetAsync(cancellationToken);

        // The glossary's mechanical calls run on their own model, not the book's. A novel translated
        // with an expensive model should not pay that price forty times over for "list the names in
        // this passage" - the two jobs need different things from a model.
        string glossaryModel = applicationSettings.glossaryModel;

        (IReadOnlyList<string> candidates, double extractionCost) = await extractor.ExtractCandidatesAsync(
            sourceTexts.Select(MarkdownText.Strip).ToList(),
            novel.sourceLanguage,
            glossaryModel,
            cancellationToken
        );

        (IReadOnlyList<string> unresolved, double resolveCost) = await glossary.ReconcileWithExistingAsync(
            novel.id,
            language,
            candidates,
            glossaryModel,
            cancellationToken
        );

        IReadOnlyList<GlossaryTerm> terms = await glossary.SelectForChapterAsync(
            novel.id,
            language,
            sourcePlainText,
            cancellationToken
        );

        IReadOnlyList<string> voice = await RecentVoiceAsync(
            novel.id,
            language,
            chapter.index,
            applicationSettings,
            cancellationToken
        );

        // Same query ChapterRepairer uses to find the profile it repairs by - a translate run
        // should match the same learned voice a repair would enforce, so the two must not drift
        // apart by reading it differently.
        VoiceProfile? voiceProfile = await database.voiceProfiles
            .AsNoTracking()
            .Where(profile => profile.novelId == novel.id && profile.language == language)
            .OrderByDescending(profile => profile.createdAt)
            .FirstOrDefaultAsync(cancellationToken);

        ChapterTranslationOutcome outcome = await translator.TranslateAsync(
            new ChapterTranslationRequest(
                novel.sourceLanguage,
                language,
                sourceTexts,
                terms,
                voice,
                applicationSettings.globalStyleGuide,
                novel.styleGuide,
                novel.normalizeQuotes,
                novel.model,
                ChunkBudget.From(applicationSettings, engine.batching),
                voiceProfile?.summary,
                passSegments: applicationSettings.passSegments,
                passContextBefore: applicationSettings.passContextBefore,
                passContextAfter: applicationSettings.passContextAfter,
                thinkingTokens: applicationSettings.thinkingTokens,
                proofread: applicationSettings.proofread,
                glossaryModel: glossaryModel,
                sourcePlainText: sourcePlainText
            ),
            text => notifier.TranslationDeltaAsync(novel.id, chapter.id, text),
            new DatabaseBatchCache(database, chapter.id, language, novel.model),
            progress,
            cancellationToken
        );

        string markdown = segmenter.Reassemble(segmented, outcome.segments);
        string plainText = string.Join("\n", outcome.segments.Select(MarkdownText.Strip));

        IReadOnlyList<TranslationIssue> issues = checks.Run(new TranslationCheckContext(
            novel.sourceLanguage,
            language,
            chapter.sourcePlainText,
            plainText,
            terms
        ));

        database.chapterTranslations.Add(new ChapterTranslation {
            chapterId = chapter.id,
            language = language,
            markdown = markdown,
            plainText = plainText,
            origin = TranslationOrigin.Ai,
            model = novel.model,
            costUsd = outcome.costUsd
        });

        await RecordIssuesAsync(chapter.id, language, issues, cancellationToken);

        chapter.translationState = ChapterTranslationState.Translated;
        chapter.glossaryState = ChapterGlossaryState.Analyzed;

        await database.SaveChangesAsync(cancellationToken);

        double settleCost = await SettleRemainingTermsAsync(
            novel.id,
            language,
            chapter.id,
            unresolved,
            sourceTexts.Select(MarkdownText.Strip).ToList(),
            outcome.segments.Select(MarkdownText.Strip).ToList(),
            glossaryModel,
            cancellationToken
        );

        return new ChapterTranslationSummary(
            chapter.id,
            outcome.segments.Count,
            outcome.costUsd + extractionCost + resolveCost + settleCost,
            candidates,
            issues
        );
    }


    // Replaces this chapter's findings for this language with the ones just made.
    //
    // Replace rather than accumulate, because the findings describe text that no longer exists.
    // Their block indices point into the previous translation, so keeping them would leave the
    // interface sending the reader to the wrong paragraph - worse than not sending them at all.
    //
    // The cost is real and worth stating: a finding the user had dismissed as a false positive comes
    // back after a re-translation. Carrying a dismissal across would mean matching old findings to
    // new text, which is guesswork, and guessing wrong here means silently hiding a genuine defect.
    // Showing it twice is the safer error.
    private async Task RecordIssuesAsync(
        long chapterId,
        string language,
        IReadOnlyList<TranslationIssue> issues,
        CancellationToken cancellationToken
    ) {
        // Removed through the change tracker rather than with ExecuteDelete, so the removal and the
        // replacements land in the same transaction as the translation itself. ExecuteDelete commits
        // on its own, which would leave a chapter with its old findings gone and no new ones if the
        // save that follows failed. There are at most a handful of rows, so loading them costs
        // nothing worth having that window for.
        List<ChapterTranslationIssue> previous = await database.chapterTranslationIssues
            .Where(issue => issue.chapterId == chapterId && issue.language == language)
            .ToListAsync(cancellationToken);

        database.chapterTranslationIssues.RemoveRange(previous);

        foreach (TranslationIssue issue in issues) {
            database.chapterTranslationIssues.Add(new ChapterTranslationIssue {
                chapterId = chapterId,
                language = language,
                check = issue.check,
                code = issue.message.code,
                message = issue.message.text,
                argsJson = IssueStatus.ArgsOf(issue.message),
                blockIndex = issue.blockIndex
            });
        }
    }


    // Samples the end of the last few translated chapters as an example of the established voice.
    //
    // Sent as source-and-translation pairs rather than translation alone. The translation on its own
    // shows the register; the pair also shows the mapping - how this translator renders an
    // honorific, how closely it follows sentence structure, where it splits a long line. That is
    // what the next chapter needs to match, and it costs only the source text to include.
    private async Task<IReadOnlyList<string>> RecentVoiceAsync(
        long novelId,
        string language,
        int beforeIndex,
        ApplicationSettings applicationSettings,
        CancellationToken cancellationToken
    ) {
        // Both halves of the window are settings: sampling more chapters and more of each buys
        // consistency and costs tokens on every single request, which is a trade only the person
        // paying for the run can make. Zero chapters turns the window off entirely.
        if (applicationSettings.voiceWindowChapters == 0 || applicationSettings.voiceWindowParagraphs == 0) {
            return [];
        }

        // Only chapters that have both sides. The window teaches the model how this book's prose was
        // rendered, which is a fact about a pair — a translation with no original beside it has
        // nothing to demonstrate.
        var passages = await database.chapterTranslations
            .AsNoTracking()
            .Where(translation => translation.language == language
                && translation.chapter!.novelId == novelId
                && translation.chapter.index < beforeIndex
                && translation.chapter.sourcePlainText != null)
            .OrderByDescending(translation => translation.chapter!.index)
            .Take(applicationSettings.voiceWindowChapters)
            .Select(translation => new {
                source = translation.chapter!.sourcePlainText!,
                translated = translation.plainText
            })
            .ToListAsync(cancellationToken);

        List<string> window = [];

        foreach (var passage in passages) {
            string[] source = Tail(passage.source, applicationSettings.voiceWindowParagraphs);
            string[] translated = Tail(passage.translated, applicationSettings.voiceWindowParagraphs);

            // Pairing is by position from the end, which lines up because both sides are segmented
            // the same way. An imported translation with a different paragraph count simply yields
            // fewer pairs rather than mismatched ones.
            int pairs = Math.Min(source.Length, translated.Length);

            if (pairs == 0) {
                continue;
            }

            StringBuilder block = new StringBuilder();

            for (int index = 0; index < pairs; index++) {
                block.Append(source[^(pairs - index)]).Append('\n');
                block.Append("-> ").Append(translated[^(pairs - index)]).Append('\n');
            }

            window.Add(block.ToString().TrimEnd());
        }

        window.Reverse();

        return window;
    }


    private static string[] Tail(string plainText, int paragraphs) {
        return plainText
            .ReplaceLineEndings("\n")
            .Split('\n')
            .Where(line => line.Trim().Length > 0)
            .TakeLast(paragraphs)
            .ToArray();
    }


    // Settles the terms that no existing translation could account for, using our own output.
    //
    // Only the paragraph the term appears in is sent, with its counterpart from the translation -
    // the two sides are segmented identically, so the index lines them up. Sending the whole
    // chapter for every term would make this cost the length of the chapter times the number of new
    // names, which on a book-length chapter with forty characters is the most expensive thing in
    // the whole pipeline and buys nothing: the answer is in one paragraph either way.
    private async Task<double> SettleRemainingTermsAsync(
        long novelId,
        string language,
        long chapterId,
        IReadOnlyList<string> unresolved,
        IReadOnlyList<string> sourceSegments,
        IReadOnlyList<string> translatedSegments,
        string model,
        CancellationToken cancellationToken
    ) {
        double costUsd = 0;

        foreach (string term in unresolved) {
            int index = IndexOfTerm(sourceSegments, term);

            if (index < 0) {
                continue;
            }

            (string? rendering, double askCost) = await extractor.ReadRenderingAsync(
                sourceSegments[index],
                index < translatedSegments.Count ? translatedSegments[index] : string.Empty,
                term,
                language,
                model,
                cancellationToken
            );

            costUsd += askCost;

            if (rendering is null) {
                continue;
            }

            await glossary.RecordAsync(
                novelId,
                language,
                term,
                rendering,
                GlossaryEntryOrigin.AiExtracted,
                chapterId,
                cancellationToken
            );
        }

        return costUsd;
    }


    private static int IndexOfTerm(IReadOnlyList<string> segments, string term) {
        for (int index = 0; index < segments.Count; index++) {
            if (segments[index].Contains(term, StringComparison.Ordinal)) {
                return index;
            }
        }

        return -1;
    }
}
