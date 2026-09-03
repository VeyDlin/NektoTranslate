using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NektoTranslate.Common.Data;
using NektoTranslate.Parsing.Contracts;
using NektoTranslate.Parsing.Entities;
using NektoTranslate.Parsing.Services;


namespace NektoTranslate.Parsing.Controllers;


[ApiController]
[Route("api/parsers")]
public class ParserScriptsController(
    NektoDbContext database,
    FileParserScriptStore files
) : ControllerBase {

    // The sites the application can read: the bundled files, annotated with whether the user has
    // edited or switched any of them off. One list rather than two, because "which sites work" is a
    // single question and splitting it would make the user reconcile the halves.
    [HttpGet]
    public async Task<IReadOnlyList<object>> List(CancellationToken cancellationToken) {
        Dictionary<string, ParserScript> stored = await database.parserScripts
            .AsNoTracking()
            .ToDictionaryAsync(script => script.hostName, StringComparer.OrdinalIgnoreCase, cancellationToken);

        List<object> entries = files.ParserScripts()
            .Select(file => Path.GetFileNameWithoutExtension(file.name))
            .Select(host => new {
                hostName = host,
                bundled = true,
                edited = stored.ContainsKey(host),
                enabled = !stored.TryGetValue(host, out ParserScript? edit) || edit.enabled
            })
            .ToList<object>();

        entries.AddRange(stored.Values
            .Where(script => !script.bundledOverride)
            .Select(script => new {
                hostName = script.hostName,
                bundled = false,
                edited = true,
                enabled = script.enabled
            }));

        return entries;
    }


    [HttpGet("{hostName}/source")]
    public async Task<ActionResult<string>> Source(string hostName, CancellationToken cancellationToken) {
        ParserScript? stored = await database.parserScripts
            .AsNoTracking()
            .FirstOrDefaultAsync(script => script.hostName == hostName, cancellationToken);

        if (stored is not null) {
            return stored.scriptSource;
        }

        ParserScriptFile? file = files.ParserScripts()
            .FirstOrDefault(candidate =>
                string.Equals(
                    Path.GetFileNameWithoutExtension(candidate.name),
                    hostName,
                    StringComparison.OrdinalIgnoreCase
                )
            );

        return file is null ? NotFound() : file.content;
    }


    [HttpPut("{hostName}")]
    public async Task<ParserScript> Upsert(
        string hostName,
        [FromBody] UpsertParserScriptRequest request,
        CancellationToken cancellationToken
    ) {
        ParserScript? script = await database.parserScripts
            .FirstOrDefaultAsync(candidate => candidate.hostName == hostName, cancellationToken);

        bool bundled = files.ParserScripts().Any(file =>
            string.Equals(
                Path.GetFileNameWithoutExtension(file.name),
                hostName,
                StringComparison.OrdinalIgnoreCase
            )
        );

        if (script is null) {
            script = new ParserScript {
                hostName = hostName,
                displayName = request.displayName ?? hostName,
                scriptSource = request.scriptSource,
                bundledOverride = bundled
            };

            database.parserScripts.Add(script);
        }

        script.displayName = request.displayName ?? script.displayName;
        script.scriptSource = request.scriptSource;
        script.enabled = request.enabled ?? script.enabled;
        script.updatedAt = DateTimeOffset.UtcNow;

        await database.SaveChangesAsync(cancellationToken);

        return script;
    }


    // Deleting an override restores the bundled parser, because the bundled one was never replaced -
    // only shadowed.
    [HttpDelete("{hostName}")]
    public async Task<ActionResult> Revert(string hostName, CancellationToken cancellationToken) {
        int removed = await database.parserScripts
            .Where(script => script.hostName == hostName)
            .ExecuteDeleteAsync(cancellationToken);

        return removed == 0 ? NotFound() : NoContent();
    }
}
