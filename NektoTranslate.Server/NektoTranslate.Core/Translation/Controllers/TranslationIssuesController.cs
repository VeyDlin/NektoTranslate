using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NektoTranslate.Common.Data;
using NektoTranslate.Translation.Entities;
using NektoTranslate.Translation.Enums;
using NektoTranslate.Translation.Services;


namespace NektoTranslate.Translation.Controllers;


// What the quality checks found, across a whole novel.
//
// A novel-level list rather than only a per-chapter one, because that is the question these findings
// exist to answer: a run of two hundred chapters finishes, and the user needs to know which handful
// deserve a second look. Asking chapter by chapter would mean two hundred requests to find the four
// that matter.
[ApiController]
[Route("api/novels/{novelId:long}/issues")]
public class TranslationIssuesController(NektoDbContext database) : ControllerBase {

    // Ordered by chapter, then by position within it, so the list reads in the order the book does
    // rather than in the order the checks happened to run.
    [HttpGet]
    public async Task<IReadOnlyList<object>> List(
        long novelId,
        [FromQuery] TranslationIssueState? state,
        [FromQuery] string? language,
        CancellationToken cancellationToken
    ) {
        IQueryable<ChapterTranslationIssue> issues = database.chapterTranslationIssues
            .AsNoTracking()
            .Where(issue => issue.chapter!.novelId == novelId);

        // Defaults to the open ones. A screen that opened on every finding ever recorded, including
        // the ones already dealt with, would be useless on its second use.
        issues = issues.Where(issue => issue.state == (state ?? TranslationIssueState.Open));

        if (!string.IsNullOrWhiteSpace(language)) {
            issues = issues.Where(issue => issue.language == language);
        }

        return await issues
            .OrderBy(issue => issue.chapter!.index)
            .ThenBy(issue => issue.blockIndex ?? int.MaxValue)
            .Select(issue => new {
                issue.id,
                issue.chapterId,
                chapterIndex = issue.chapter!.index,
                chapterTitle = issue.chapter.title,
                issue.language,
                issue.check,
                message = IssueStatus.Rebuild(issue.code, issue.message, issue.argsJson),
                issue.blockIndex,
                issue.state,
                issue.createdAt,
                issue.closedAt
            })
            .ToListAsync<object>(cancellationToken);
    }


    // How many are outstanding, per check. Cheap enough to poll after a run, and it is what a badge
    // in the interface needs rather than the findings themselves.
    [HttpGet("summary")]
    public async Task<IReadOnlyList<object>> Summary(long novelId, CancellationToken cancellationToken) {
        return await database.chapterTranslationIssues
            .AsNoTracking()
            .Where(issue => issue.chapter!.novelId == novelId && issue.state == TranslationIssueState.Open)
            .GroupBy(issue => issue.check)
            .Select(group => new {
                check = group.Key,
                count = group.Count(),
                chapters = group.Select(issue => issue.chapterId).Distinct().Count()
            })
            .ToListAsync<object>(cancellationToken);
    }


    // Resolved means the text was changed; dismissed means the finding was wrong. The distinction is
    // the user's to make and is kept, because it is the only evidence there will ever be about
    // whether a check earns its place.
    [HttpPost("{issueId:long}/resolve")]
    public Task<ActionResult> Resolve(long novelId, long issueId, CancellationToken cancellationToken) {
        return CloseAsync(novelId, issueId, TranslationIssueState.Resolved, cancellationToken);
    }


    [HttpPost("{issueId:long}/dismiss")]
    public Task<ActionResult> Dismiss(long novelId, long issueId, CancellationToken cancellationToken) {
        return CloseAsync(novelId, issueId, TranslationIssueState.Dismissed, cancellationToken);
    }


    [HttpPost("{issueId:long}/reopen")]
    public async Task<ActionResult> Reopen(long novelId, long issueId, CancellationToken cancellationToken) {
        ChapterTranslationIssue? issue = await FindAsync(novelId, issueId, cancellationToken);

        if (issue is null) {
            return NotFound();
        }

        issue.state = TranslationIssueState.Open;
        issue.closedAt = null;

        await database.SaveChangesAsync(cancellationToken);

        return NoContent();
    }


    private async Task<ActionResult> CloseAsync(
        long novelId,
        long issueId,
        TranslationIssueState state,
        CancellationToken cancellationToken
    ) {
        ChapterTranslationIssue? issue = await FindAsync(novelId, issueId, cancellationToken);

        if (issue is null) {
            return NotFound();
        }

        issue.state = state;
        issue.closedAt = DateTimeOffset.UtcNow;

        await database.SaveChangesAsync(cancellationToken);

        return NoContent();
    }


    // Scoped through the novel rather than looked up by id alone, so an id from another book cannot
    // be closed through this route.
    private Task<ChapterTranslationIssue?> FindAsync(
        long novelId,
        long issueId,
        CancellationToken cancellationToken
    ) {
        return database.chapterTranslationIssues
            .FirstOrDefaultAsync(
                issue => issue.id == issueId && issue.chapter!.novelId == novelId,
                cancellationToken
            );
    }
}
