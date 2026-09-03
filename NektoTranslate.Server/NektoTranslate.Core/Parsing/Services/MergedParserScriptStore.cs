using Microsoft.EntityFrameworkCore;
using NektoTranslate.Common.Data;
using NektoTranslate.Parsing.Entities;


namespace NektoTranslate.Parsing.Services;


// The parser set the runner actually sees: the bundled files, with the user's edits layered on top.
//
// Bundled parsers stay on disk and are never copied into the database. Copying them would mean an
// upstream update needed a migration, and a user who had edited one would have no way to tell their
// change from the original. Only differences are stored, which also makes "revert to the bundled
// version" a delete rather than a restore.
//
// A stored script is appended after the file it replaces. The parser factory keeps the last
// registration for a host name, so ordering is what makes an override take effect - no unloading,
// no bookkeeping.
public class MergedParserScriptStore(
    FileParserScriptStore files,
    NektoDbContext database
) : IParserScriptStore {

    public IReadOnlyList<ParserScriptFile> CoreScripts() {
        return files.CoreScripts();
    }


    public IReadOnlyList<ParserScriptFile> ParserScripts() {
        List<ParserScript> stored = database.parserScripts.AsNoTracking().ToList();

        if (stored.Count == 0) {
            return files.ParserScripts();
        }

        // Every stored entry replaces its bundled file, enabled or not.
        //
        // Dropping the file is what makes an override take effect at all: the upstream factory
        // throws on a duplicate host registration, so a bundled file left in place would claim the
        // host first and the override's own registration would fail - silently, because a script
        // that fails to evaluate is skipped rather than fatal. Appending after the file is not
        // enough; the file has to go.
        //
        // A disabled entry drops the file and contributes nothing, which is what switching a
        // parser off has to mean.
        HashSet<string> replaced = stored
            .Select(script => script.hostName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<ParserScriptFile> merged = files.ParserScripts()
            .Where(file => !replaced.Contains(Path.GetFileNameWithoutExtension(file.name)))
            .ToList();

        merged.AddRange(stored
            .Where(script => script.enabled)
            .Select(script => new ParserScriptFile($"db:{script.hostName}", script.scriptSource))
        );

        return merged;
    }
}
