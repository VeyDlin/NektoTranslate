using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NektoTranslate.Common.Data;
using NektoTranslate.Jobs.Contracts;
using NektoTranslate.Jobs.Entities;
using NektoTranslate.Jobs.Enums;


namespace NektoTranslate.Jobs.Controllers;


// Everything that is happening to a novel right now, in one answer.
//
// This is what a screen asks on arrival. Live events only reach a page that was open when they were
// sent; a page opened mid-run, or reloaded, has to be told where things stand - which runs are
// going, how far, what each chapter has done so far - or it shows a start form over a run that is
// already halfway through.
//
// Imports are also what just happened, not only what is happening: behind each kind that has no live
// run, the last run to settle, unless its report was dismissed. A report is read after the run, often
// from a page opened later, and it is the reader who puts it away - not the closing of a tab.
[ApiController]
[Route("api/novels/{novelId:long}/activity")]
public class ActivityController(NektoDbContext database) : ControllerBase {

    public sealed record ActivityView(
        TranslationJob? translation,
        IReadOnlyList<ImportJobView> imports
    );


    [HttpGet]
    public async Task<ActivityView> Get(long novelId, CancellationToken cancellationToken) {
        TranslationJob? translation = await database.translationJobs
            .AsNoTracking()
            .Where(job => job.novelId == novelId
                && (job.state == JobState.Queued || job.state == JobState.Running || job.state == JobState.Paused))
            .OrderByDescending(job => job.createdAt)
            .FirstOrDefaultAsync(cancellationToken);

        List<ImportJob> imports = await database.importJobs
            .AsNoTracking()
            .Include(job => job.items)
            .Where(job => job.novelId == novelId
                && (job.state == JobState.Queued || job.state == JobState.Running || job.state == JobState.Paused))
            .OrderBy(job => job.createdAt)
            .ToListAsync(cancellationToken);

        foreach (ImportKind kind in Enum.GetValues<ImportKind>()) {
            if (imports.Any(job => job.kind == kind)) {
                continue;
            }

            ImportJob? settled = await LastSettledAsync(novelId, kind, cancellationToken);

            if (settled is not null && settled.dismissedAt is null) {
                imports.Add(settled);
            }
        }

        return new ActivityView(
            translation,
            imports.Select(job => ImportJobView.Of(job, withItems: true)).ToList()
        );
    }


    // Only the latest settled run of a kind is ever a candidate, dismissed or not. An older one was
    // superseded the moment a newer run started, and must not come back when the newer one is put
    // away.
    private Task<ImportJob?> LastSettledAsync(long novelId, ImportKind kind, CancellationToken cancellationToken) {
        return database.importJobs
            .AsNoTracking()
            .Include(job => job.items)
            .Where(job => job.novelId == novelId
                && job.kind == kind
                && (job.state == JobState.Completed || job.state == JobState.Failed || job.state == JobState.Cancelled))
            .OrderByDescending(job => job.createdAt)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
