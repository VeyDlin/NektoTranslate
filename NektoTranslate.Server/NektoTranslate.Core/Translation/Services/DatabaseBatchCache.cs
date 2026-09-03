using Microsoft.EntityFrameworkCore;
using NektoTranslate.Common.Data;
using NektoTranslate.Translation.Entities;


namespace NektoTranslate.Translation.Services;


// Batch cache bound to one chapter and one target language.
//
// Each batch is committed as soon as it comes back, not at the end of the chapter. That ordering is
// the whole point: a run that dies on the fifteenth of seventeen requests keeps fourteen, and the
// retry pays only for what is actually missing.
public class DatabaseBatchCache(
    NektoDbContext database,
    long chapterId,
    string language,
    string model
) : IBatchCache {

    public async Task<string?> TryGetAsync(string sourceHash, CancellationToken cancellationToken = default) {
        return await database.chapterChunks
            .AsNoTracking()
            .Where(chunk => chunk.chapterId == chapterId
                && chunk.language == language
                && chunk.sourceHash == sourceHash)
            .Select(chunk => chunk.translatedText)
            .FirstOrDefaultAsync(cancellationToken);
    }


    public async Task StoreAsync(
        string sourceHash,
        int index,
        string translatedText,
        double costUsd,
        CancellationToken cancellationToken = default
    ) {
        bool exists = await database.chapterChunks.AnyAsync(
            chunk => chunk.chapterId == chapterId
                && chunk.language == language
                && chunk.sourceHash == sourceHash,
            cancellationToken
        );

        if (exists) {
            return;
        }

        database.chapterChunks.Add(new ChapterChunk {
            chapterId = chapterId,
            language = language,
            sourceHash = sourceHash,
            index = index,
            translatedText = translatedText,
            model = model,
            costUsd = costUsd
        });

        // Committed on its own rather than with the chapter, so an interruption before the chapter
        // finishes still leaves this batch recorded. Saving it together with the chapter would make
        // the cache worthless for exactly the case it exists for.
        await database.SaveChangesAsync(cancellationToken);
    }
}
