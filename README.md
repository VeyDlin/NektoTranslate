# NektoTranslate

NektoTranslate is a local tool that fetches a web novel from its source site and translates it
chapter by chapter through the Claude Code CLI, running on your own Claude subscription. It runs as
a single process on your own machine and keeps everything it produces - the book, the glossary,
every translated chapter - on your own computer; there is no NektoTranslate server anywhere for it
to talk to.

It is built as a .NET server that serves a Vue client out of the same process, plus an optional
Tauri window (`NektoTranslate.Desktop`) that wraps the two in a native shell instead of a browser
tab. The desktop shell is a convenience on top of the server, not a different application - running
the published server on its own and opening it in a browser works exactly the same way.

**Credits:** the site parsers under `NektoTranslate.Server/vendor/WebToEpub` are the parsing scripts
from [WebToEpub](https://github.com/dteviot/WebToEpub), David Teviotdale's project for reading web
novels into epub files. They are used here under their own GPL-3.0 licence and with thanks -
unmodified except where a comment inside that folder says otherwise.

## Licence

NektoTranslate is licensed under the GNU General Public License, version 3 or later
(GPL-3.0-or-later) - see [`LICENSE`](LICENSE). This follows from the WebToEpub parsers above: they
are GPL-3.0, and shipping them as part of a distributed application places the whole of that
distributed work under the same terms. Nothing about NektoTranslate's own code called for a copyleft
licence on its own; the parsers are why.

## Releases

The application's version lives in exactly one place: `<Version>` in
`NektoTranslate.Server/Directory.Build.props`. Everything else - the desktop shell's own version
files, the published assembly's informational version, the health endpoint, the version shown in the
client - derives from it; see `NektoTranslate.Desktop/scripts/sync-version.mjs` for how the desktop
files stay in step.

`.github/workflows/release.yml` watches `main`. A push that leaves the version unchanged does
nothing. A push that changes it builds the Windows and Linux desktop installers and a standalone
server zip for each platform, and - only once every build has succeeded - tags the commit
`v<version>`, creates a GitHub release from it, and writes a changelog into the release body from the
commit history since the previous release. A failed build leaves no tag and no release behind, so
the next push simply tries again.

## Prerequisites

- The .NET 10 runtime (or SDK, for building).
- The [Claude Code CLI](https://docs.claude.com/en/docs/claude-code) installed and logged in - this
  application calls it as a subprocess for every translation.
- Node.js, but only if you are building the client yourself; a published release already has it
  built in.

## Publishing

From the repository root:

```
dotnet publish NektoTranslate.Server/NektoTranslate.Api -c Release -o <folder>
```

This builds the client and copies it into the server's `wwwroot`, then publishes a
framework-dependent build to `<folder>` - it needs the .NET 10 runtime already installed on the
machine that runs it. For a self-contained build that needs nothing preinstalled, add a runtime
identifier:

```
dotnet publish NektoTranslate.Server/NektoTranslate.Api -c Release -r win-x64 --self-contained -o <folder>
```

(`linux-x64`, `osx-x64` and `osx-arm64` work the same way.)

## Running

From the published folder:

```
NektoTranslate.Api
```

Then open the URL it prints - `http://127.0.0.1:5080` by default. The server binds to loopback
only: there is no login, because there is no second user, and nothing here is meant to be reached
from the network.

Two flags cover the cases the defaults do not:

- `--ServerUrl=http://127.0.0.1:5099` - move off the default port, or bind to a different loopback
  address.
- `--DataDirectory=<path>` - keep the database and everything else this application stores
  somewhere other than the platform default below, for example a library kept on another drive.

## Where the data lives

Unless overridden with `--DataDirectory`, the database, the downloaded browser and everything else
this application owns live in:

| Platform | Location |
|---|---|
| Windows | `%LOCALAPPDATA%\NektoTranslate` |
| macOS | `~/Library/Application Support/NektoTranslate` |
| Linux | `$XDG_DATA_HOME/NektoTranslate`, or `~/.local/share/NektoTranslate` if that is unset |

Nothing here is opened in a browser automatically when the server starts.

## Desktop

`NektoTranslate.Desktop` wraps the server in an optional native window (Tauri 2) instead of a
browser tab - it starts the server as a child process and stops it when the window closes, or
attaches to one already running for development. It does not change anything about running the
server on its own, above. See `NektoTranslate.Desktop/README.md` for how to run and build it.
