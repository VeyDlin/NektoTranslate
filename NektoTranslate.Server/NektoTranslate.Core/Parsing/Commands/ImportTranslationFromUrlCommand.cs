using Mediator;
using Microsoft.Extensions.Logging;
using NektoTranslate.Common.Contracts;
using NektoTranslate.Parsing.Contracts;
using NektoTranslate.Parsing.Services;
using NektoTranslate.Translation.Contracts;
using NektoTranslate.Translation.Services;


namespace NektoTranslate.Parsing.Commands;


public record ImportTranslationFromUrlCommand(
    long novelId,
    string language,
    // Where the first selected entry lands. Everything after it follows in order.
    //
    // This one number is what handles the common failure: a translation site whose first entry is a
    // translator's note rather than chapter one. Import from index 0 and everything is one place out
    // for the rest of the book; the user either starts at -1 worth of offset here, or imports and
    // corrects afterwards with a move. Both routes exist because neither is always the obvious one
    // at the moment of importing - the note is often only recognisable once its text is visible.
    int startAtChapterIndex,
    IReadOnlyList<ParsedChapterLink> chapters
) : ICommand<TranslationImportResult>;


// Fetches an existing translation and attaches it to chapters the novel already has.
//
// The mirror of ImportFromUrlCommand, and deliberately built the same way: the same parser fetches
// the page, and the result goes through the same sanitising and Markdown conversion, so an imported
// translation is stored in exactly the shape our own output is. Anything else would make the two
// incomparable, and comparing them paragraph by paragraph is the entire point - it is how the
// glossary learns what the previous translator called each character.
public class ImportTranslationFromUrlCommandHandler(
    ISiteParser parser,
    ITranslationImportService importer,
    ILogger<ImportTranslationFromUrlCommandHandler> logger
) : ICommandHandler<ImportTranslationFromUrlCommand, TranslationImportResult> {

    public async ValueTask<TranslationImportResult> Handle(
        ImportTranslationFromUrlCommand command,
        CancellationToken cancellationToken
    ) {
        List<ImportedTranslation> fetched = [];
        List<TranslationImportRejection> failures = [];
        int position = 0;

        foreach (ParsedChapterLink link in command.chapters) {
            cancellationToken.ThrowIfCancellationRequested();

            int chapterIndex = command.startAtChapterIndex + position;
            position++;

            try {
                ParsedChapter chapter = await parser.GetChapterAsync(link.sourceUrl, cancellationToken);

                fetched.Add(new ImportedTranslation(chapterIndex, chapter.html, link.sourceUrl));
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception failure) {
                logger.LogWarning(failure, "Could not fetch translation {Url}", link.sourceUrl);

                // The position is still consumed. Skipping it instead would pull every later chapter
                // one place forward to fill the gap, quietly misaligning the rest of the import
                // because one page happened to time out.
                failures.Add(new TranslationImportRejection(
                    chapterIndex,
                    Statuses.ChapterFetchFailed.With(("url", link.sourceUrl), ("reason", failure.Message))
                ));
            }
        }

        TranslationImportResult result = await importer.ImportAsync(
            command.novelId,
            command.language,
            fetched,
            cancellationToken
        );

        return result with { rejected = [.. failures, .. result.rejected] };
    }
}
