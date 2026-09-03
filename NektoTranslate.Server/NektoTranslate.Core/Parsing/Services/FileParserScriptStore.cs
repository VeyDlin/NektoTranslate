namespace NektoTranslate.Parsing.Services;


public interface IParserScriptStore {

    // Core scripts, in the order they must be evaluated. Later files reference classes the earlier
    // ones define, so the order is part of the contract rather than a convenience.
    IReadOnlyList<ParserScriptFile> CoreScripts();


    IReadOnlyList<ParserScriptFile> ParserScripts();
}


public sealed record ParserScriptFile(string name, string content);


// Reads the parser scripts from disk.
//
// Kept as loose files rather than compiled resources on purpose: adding support for a new site
// should be dropping in a small script, and the user is meant to be able to edit one in place and
// see the result without a rebuild.
public class FileParserScriptStore(string rootDirectory) : IParserScriptStore {

    // The load order the upstream extension uses. Files that only exist to drive its popup are
    // omitted - they reach for elements of a page that does not exist here, and nothing a parser
    // calls depends on them.
    private static readonly string[] coreOrder = [
        "UIText.js",
        "UserPreferences.js",
        "EpubMetaInfo.js",
        "ErrorLog.js",
        "Util.js",
        "FootnoteExtractor.js",
        "Download.js",
        "HttpClient.js",
        "EpubItem.js",
        "ParserFactory.js",
        "ImageCollector.js",
        // Needed even though nothing here collects images: one parser registers a URL rule whose
        // predicate closes over Imgur, and that predicate runs on every lookup that is not an exact
        // host match. Leave it out and any unrecognised URL throws instead of returning "no parser".
        "Imgur.js",
        "Parser.js"
    ];

    // Base classes other parsers extend. They must be evaluated before the parsers that name them.
    private static readonly string[] baseParsers = [
        "WordpressBaseParser.js",
        "MadaraParser.js"
    ];


    public IReadOnlyList<ParserScriptFile> CoreScripts() {
        List<ParserScriptFile> scripts = [];

        foreach (string name in coreOrder) {
            string path = Path.Combine(rootDirectory, "js", name);

            if (File.Exists(path)) {
                scripts.Add(new ParserScriptFile(name, File.ReadAllText(path)));
            }
        }

        return scripts;
    }


    public IReadOnlyList<ParserScriptFile> ParserScripts() {
        string directory = Path.Combine(rootDirectory, "js", "parsers");

        if (!Directory.Exists(directory)) {
            return [];
        }

        List<ParserScriptFile> scripts = [];

        foreach (string name in baseParsers) {
            string path = Path.Combine(directory, name);

            if (File.Exists(path)) {
                scripts.Add(new ParserScriptFile(name, File.ReadAllText(path)));
            }
        }

        HashSet<string> loaded = scripts.Select(script => script.name).ToHashSet(StringComparer.Ordinal);

        foreach (string path in Directory.EnumerateFiles(directory, "*.js").OrderBy(path => path, StringComparer.Ordinal)) {
            string name = Path.GetFileName(path);

            if (loaded.Add(name)) {
                scripts.Add(new ParserScriptFile(name, File.ReadAllText(path)));
            }
        }

        return scripts;
    }
}
