using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NektoTranslate.Chapters.Commands;
using NektoTranslate.Chapters.Contracts;
using NektoTranslate.Chapters.Entities;
using NektoTranslate.Common.Data;
using NektoTranslate.Translation.Enums;
using NektoTranslate.Translation.Services;


namespace NektoTranslate.Chapters.Controllers;


[ApiController]
[Route("api/novels/{novelId:long}/chapters")]
public class ChaptersController(IMediator mediator, NektoDbContext database) : ControllerBase {

    [HttpPost("import")]
    public async Task<IReadOnlyList<Chapter>> Import(
        long novelId,
        [FromBody] IReadOnlyList<ImportedChapter> chapters,
        CancellationToken cancellationToken
    ) {
        return await mediator.Send(new ImportChaptersCommand(novelId, chapters), cancellationToken);
    }


    // The list deliberately omits chapter bodies. A thousand-chapter novel would otherwise send
    // several megabytes of prose to render a table of contents.
    [HttpGet]
    public async Task<IReadOnlyList<object>> List(long novelId, CancellationToken cancellationToken) {
        string language = await database.novels
            .Where(novel => novel.id == novelId)
            .Select(novel => novel.targetLanguage)
            .FirstAsync(cancellationToken);

        return await database.chapters
            .AsNoTracking()
            .Where(chapter => chapter.novelId == novelId)
            .OrderBy(chapter => chapter.index)
            .Select(chapter => new {
                chapter.id,
                chapter.index,
                chapter.title,
                chapter.glossaryState,
                chapter.translationState,

                // Whether there is anything to translate from. The source itself is deliberately not
                // sent - that is the megabytes this projection exists to avoid - but its absence is
                // a fact the list needs: it decides what a run can be asked to do with the chapter.
                hasOriginal = chapter.sourceMarkdown != null,

                // Whether a rendering is on file, apart from the state. A chapter reads Failed after
                // a repair or a forced re-translation broke on it, and still has the rendering it had
                // before - repair and learning can take it; the state alone said they could not.
                hasTranslation = database.chapterTranslations.Any(translation =>
                    translation.chapterId == chapter.id && translation.language == language)
            })
            .ToListAsync<object>(cancellationToken);
    }


    // Removing a chapter takes its translations and cached batches with it. Needed because an
    // import can pull the wrong range, or duplicate one, and there is no other way to undo that.
    [HttpDelete("{chapterId:long}")]
    public async Task<ActionResult> Delete(
        long novelId,
        long chapterId,
        CancellationToken cancellationToken
    ) {
        int removed = await database.chapters
            .Where(chapter => chapter.novelId == novelId && chapter.id == chapterId)
            .ExecuteDeleteAsync(cancellationToken);

        return removed == 0 ? NotFound() : NoContent();
    }


    [HttpGet("{chapterId:long}")]
    public async Task<ActionResult<object>> Get(
        long novelId,
        long chapterId,
        CancellationToken cancellationToken
    ) {
        var chapter = await database.chapters
            .AsNoTracking()
            .Where(candidate => candidate.novelId == novelId && candidate.id == chapterId)
            .Select(candidate => new {
                candidate.id,
                candidate.index,
                candidate.title,
                candidate.sourceMarkdown,
                candidate.glossaryState,
                candidate.translationState,
                translations = candidate.translations
                    .OrderByDescending(translation => translation.createdAt)
                    .Select(translation => new {
                        translation.id,
                        translation.language,
                        translation.markdown,
                        translation.origin,
                        translation.costUsd,
                        translation.createdAt
                    }),

                // Carried with the chapter rather than fetched separately: the reader needs them at
                // the moment it renders the blocks, and a second request would make showing a
                // marker beside a paragraph depend on a race.
                //
                // ToList is required, not stylistic. A collection in a final projection has to be
                // IEnumerable; left as the IQueryable this subquery returns, EF refuses the whole
                // query at runtime rather than at compile time.
                issues = database.chapterTranslationIssues
                    .Where(issue => issue.chapterId == candidate.id
                        && issue.state == TranslationIssueState.Open)
                    .OrderBy(issue => issue.blockIndex ?? int.MaxValue)
                    .Select(issue => new {
                        issue.id,
                        issue.language,
                        issue.check,
                        message = IssueStatus.Rebuild(issue.code, issue.message, issue.argsJson),
                        issue.blockIndex,
                        issue.state
                    })
                    .ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);

        return chapter is null ? NotFound() : chapter;
    }
}
