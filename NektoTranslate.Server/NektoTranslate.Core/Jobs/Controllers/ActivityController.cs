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

        return new ActivityView(
            translation,
            imports.Select(job => ImportJobView.Of(job, withItems: true)).ToList()
        );
    }
}
