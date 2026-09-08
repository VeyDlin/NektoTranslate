# NektoTranslate

A local application that fetches a novel from a source site and translates it chapter by chapter
with the Claude Code CLI. It runs as a single process on your own machine, with the client built
into the server, and stores nothing anywhere but your own computer.

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
