using Mediator;
using Microsoft.Extensions.Logging;
using NektoTranslate.Chapters.Contracts;
using NektoTranslate.Chapters.Entities;
using NektoTranslate.Chapters.Services;
using NektoTranslate.Parsing.Contracts;
using NektoTranslate.Parsing.Services;


namespace NektoTranslate.Parsing.Commands;


public record ImportFromUrlCommand(
    long novelId,
    IReadOnlyList<ParsedChapterLink> chapters
) : ICommand<ImportFromUrlResult>;


// Fetches the named chapters and hands them to the ordinary import path, so parsed content is
// sanitized and projected exactly like a manual paste - one door in, one set of rules.
//
// Takes an explicit list rather than a table-of-contents URL: the user picks which chapters they
// want from the contents they were shown, and a novel with two thousand entries must never be
// pulled down wholesale because someone pasted a link.
//
// A chapter that fails to fetch is reported and skipped. Losing the whole batch because one page
// timed out would be worse than importing forty-nine of fifty and saying which one is missing.
public class ImportFromUrlCommandHandler(
    ISiteParser parser,
    IChapterImportService importer,
    ILogger<ImportFromUrlCommandHandler> logger
) : ICommandHandler<ImportFromUrlCommand, ImportFromUrlResult> {

    public async ValueTask<ImportFromUrlResult> Handle(
        ImportFromUrlCommand command,
        CancellationToken cancellationToken
    ) {
        List<ImportedChapter> fetched = [];
        List<string> failures = [];

        foreach (ParsedChapterLink link in command.chapters) {
            cancellationToken.ThrowIfCancellationRequested();

            try {
                ParsedChapter chapter = await parser.GetChapterAsync(link.sourceUrl, cancellationToken);

                fetched.Add(new ImportedChapter(
                    string.IsNullOrWhiteSpace(link.title) ? chapter.title : link.title,
                    chapter.html,
                    null,
                    link.sourceUrl
                ));
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception failure) {
                logger.LogWarning(failure, "Could not fetch {Url}", link.sourceUrl);
                failures.Add($"{link.sourceUrl}: {failure.Message}");
            }
        }

        IReadOnlyList<Chapter> imported = await importer.ImportAsync(
            command.novelId,
            fetched,
            cancellationToken
        );

        return new ImportFromUrlResult(imported.Count, failures);
    }
}
