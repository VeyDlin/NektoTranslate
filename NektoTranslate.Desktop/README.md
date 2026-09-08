# NektoTranslate.Desktop

A thin Tauri 2 shell around `NektoTranslate.Api`. It does not replace the server: running the
published server folder directly and opening it in a browser (see the root README) works exactly
as it always has. This is an optional native window on top of that - it starts the server as a
child process, shows its pages in a window instead of a browser tab, and stops it again when the
window closes.

It can also **attach** to a server started by hand instead of spawning one, so the two can be
developed and debugged apart.

## Prerequisites

Everything the root README lists, plus:

- Rust (stable, MSVC toolchain on Windows).
- The [Tauri 2 CLI](https://v2.tauri.app), pulled in as a devDependency by `npm install` below -
  nothing to install globally.
- The WebView2 runtime on Windows (present on any current Windows install).

## Dev

```
npm install
npm run dev
```

`npm run dev` is `tauri dev`. With nothing else given, a debug build attaches to the client's own
Vite dev server on `http://127.0.0.1:5173` instead of spawning anything - start that
(`npm run dev` in `NektoTranslate.Client`) and the API (`dotnet run` in
`NektoTranslate.Server/NektoTranslate.Api`, or via the root README) yourself first, the same as
developing the client alone.

To exercise the real spawn path from a debug build - useful for testing the shell's own server
handling without a full release build - publish the server once and give an explicit data
directory, which is read as "spawn" even in a debug build (see Settings below):

```
npm run server:publish
$env:NEKTO_DATA_DIRECTORY = "C:\some\scratch\folder"
npm run dev
```

## Build

```
npm install
npm run build
```

This runs `scripts/publish-server.mjs` - a self-contained `dotnet publish` of `NektoTranslate.Api`
for the current platform into `src-tauri/server/` (which also builds the client, the same as an
ordinary server publish) - and then `tauri build`.

On Windows the result is an NSIS installer at
`src-tauri/target/release/bundle/nsis/NektoTranslate_<version>_x64-setup.exe` (an MSI is also
produced alongside it, under `bundle/msi/`, since `tauri.conf.json`'s `bundle.targets` is `"all"`),
and the plain executable at `src-tauri/target/release/NektoTranslate.exe`, which runs only beside
the `server/` folder the build copies next to it - it is not a standalone download. macOS and Linux
land under the matching `bundle/dmg`, `bundle/deb`, `bundle/rpm` and `bundle/appimage` folders.

To build for a platform other than the current machine:

```
node scripts/publish-server.mjs --rid=osx-arm64
npx tauri build --target aarch64-apple-darwin
```

`--rid` accepts `win-x64`, `osx-arm64`, `osx-x64` or `linux-x64`, and defaults to whatever the
current machine is.

To stamp a version - what CI does - set `NEKTO_BUILD_VERSION` before running `npm run build`; it
reaches `-p:Version=` on the server publish. `tauri.conf.json`'s own `version` field is the shell's
own version number (shown in the installer and the bundle's metadata) and is not derived from
this - bump it by hand for a release, or template it the way CI's tag does.

Passing a version straight through, as `npm run build -- -p:Version=1.2.3`, does **not** reach the
server publish: npm appends trailing arguments to the end of the whole `publish-server.mjs &&
tauri build` command line, so they land on `tauri build` instead of the first half. This is why the
version (and, for cross-platform builds, the RID) travels through the environment instead -
`--target`, by contrast, genuinely belongs on `tauri build`, and reaches it exactly that way in
`.github/workflows/desktop-release.yml`.

## Settings

What the shell does is resolved once at startup, highest precedence first:

1. Command line: `--server-url <url>`, `--data-directory <path>`
2. Environment: `NEKTO_SERVER_URL`, `NEKTO_DATA_DIRECTORY`
3. Nothing given: a debug build attaches to `http://127.0.0.1:5173`; a release build spawns its
   own server, with the platform default data directory.

A server url always means "attach, don't spawn." A data directory only means anything to a
spawned server, so giving one - without a server url - is read as asking for spawn mode even in a
debug build; see Dev above for why that matters.

## Attach mode

No child process. The shell polls `<url>/api/health` for up to 30 seconds - falling back to
`<url>/`, for a dev server that has no `/api/health` of its own to answer - then opens the window
there. If nothing answers either one, a native dialog names the URLs it tried and the process
exits 1. Closing the window in attach mode never touches the server it was pointed at: nothing
here started it, so nothing here stops it either.

## Spawn mode

The server binary (`server/NektoTranslate.Api.exe` on Windows, `server/NektoTranslate.Api`
elsewhere) is looked up under Tauri's bundled resource directory in a release build, and under
`src-tauri/server/` for a `cargo`/`tauri dev` run - the same place `npm run server:publish` writes
it. The shell binds an ephemeral loopback port and passes it as `--ServerUrl`, passes the resolved
data directory as `--DataDirectory` (so the two sides can never disagree about where the library
lives), and passes its own pid as `--ParentPid`. The server's own environment is inherited
otherwise - unchanged, so the Claude Code CLI on `PATH` and logged in from `~/.claude` is exactly
as reachable as it is from a terminal.

The child's stdout and stderr go to `<data directory>/logs/server.log` (the previous run's file is
kept as `server.previous.log` first); the shell never shows or inherits a console for it. The
shell waits up to 60 seconds for `/api/health` to answer 200 before opening the window; on
timeout, the child is killed and a native dialog names the log file.

On any exit path - the window closing, or the shell quitting for any other reason - the child is
killed. If the shell itself is killed without the chance to run that (a crash, a forced kill), the
server's own `--ParentPid` watchdog notices within a couple of seconds and stops itself; it is what
keeps a closed shell from leaving an orphaned server.

## Window

Title `NektoTranslate`, 1280×860 by default with a 900×600 minimum, remembered across runs
(`tauri-plugin-window-state`). F11 toggles fullscreen and Escape leaves it - the reading mode
issue #21 asks for - injected into the page itself, since the client is never touched for this.
Navigation is pinned to the server's own origin: a link to another host (a source site being
imported from, say) opens in the system browser instead of taking over the window. A second launch
focuses the existing window instead of starting another (`tauri-plugin-single-instance`).

## Next (not in this task)

- A tray icon, so closing the window does not have to mean quitting.
- An updater.
- Code signing on every platform - every installer here is unsigned, and Windows and macOS will
  both warn on first run.
