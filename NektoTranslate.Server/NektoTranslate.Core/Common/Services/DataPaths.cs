using System.Runtime.InteropServices;


namespace NektoTranslate.Common.Services;


// Where this application's own data lives, on whichever platform it is running on.
//
// Resolved once, at startup, into named paths rather than string literals repeated wherever a
// database, a browser download or a log file is opened - a folder that moved would otherwise be a
// search-and-replace across the codebase instead of an edit here.
public static class DataPaths {

    public const string DatabaseFileName = "nekto.db";

    public const string BrowsersDirectoryName = "browsers";

    public const string BrowserStateFileName = "browserState.json";

    public const string LogsDirectoryName = "logs";


    // The resolved root plus everything under it. A record rather than separate return values, so
    // a caller that only needs the database path still gets one thing to pass around instead of
    // four.
    public sealed record Layout(
        string root,
        string database,
        string browsers,
        string browserState,
        string logs
    );


    // `configured` is `DataDirectory` from configuration - blank by default, which is the ordinary
    // case, and an absolute or relative path when someone wants the library kept somewhere else.
    //
    // `platform` exists only so the three branches below can be exercised from one machine in a
    // test; production code always leaves it null and gets the platform actually running.
    public static Layout Resolve(string? configured, OSPlatform? platform = null) {
        string root = ResolveRoot(configured, platform ?? RunningPlatform());

        return new Layout(
            root: root,
            database: Path.Combine(root, DatabaseFileName),
            browsers: Path.Combine(root, BrowsersDirectoryName),
            browserState: Path.Combine(root, BrowserStateFileName),
            logs: Path.Combine(root, LogsDirectoryName)
        );
    }


    private static string ResolveRoot(string? configured, OSPlatform platform) {
        if (configured is not null && configured.Trim().Length > 0) {
            return Path.GetFullPath(configured.Trim());
        }

        if (platform == OSPlatform.Windows) {
            // Unchanged from the path this application has always used, so an existing
            // installation keeps its database instead of waking up empty next to a new one.
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NektoTranslate"
            );
        }

        if (platform == OSPlatform.OSX) {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library",
                "Application Support",
                "NektoTranslate"
            );
        }

        // Linux and anything else POSIX-shaped. The XDG base directory specification is what a
        // distribution's own applications follow, and $HOME/.local/share is the specification's
        // own fallback for a session that never set XDG_DATA_HOME.
        string? xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");

        string dataHome = xdgDataHome is not null && xdgDataHome.Trim().Length > 0
            ? xdgDataHome.Trim()
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

        return Path.Combine(dataHome, "NektoTranslate");
    }


    private static OSPlatform RunningPlatform() {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            return OSPlatform.Windows;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            return OSPlatform.OSX;
        }

        return OSPlatform.Linux;
    }
}
