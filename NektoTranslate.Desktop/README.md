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

This runs `scripts/sync-version.mjs` - stamps `tauri.conf.json`, `Cargo.toml` and this package's own
`package.json` with the version read from `NektoTranslate.Server/Directory.Build.props`, the
project's single source of truth (see the root README's Releases section) - then
`scripts/publish-server.mjs`, a self-contained `dotnet publish` of `NektoTranslate.Api` for the
current platform into `src-tauri/server/` (which also builds the client, the same as an ordinary
server publish), and finally `tauri build`.

On Windows the result is an NSIS installer at
`src-tauri/target/release/bundle/nsis/NektoTranslate_<version>_x64-setup.exe` (an MSI is also
produced alongside it, under `bundle/msi/`, since `tauri.conf.json`'s `bundle.targets` is `"all"`),
and the plain executable at `src-tauri/target/release/NektoTranslate.exe`, which runs only beside
the `server/` folder the build copies next to it - it is not a standalone download. macOS and Linux
land under the matching `bundle/dmg`, `bundle/deb`, `bundle/rpm` and `bundle/appimage` folders.

Both Windows installers install per machine, into Program Files, and ask for elevation once. This
is deliberate: a per-user NSIS install would land in `%LOCALAPPDATA%\NektoTranslate`, which is the
folder the server keeps the database and the downloaded browser in, and a program folder and a data
folder must not be the same place.

To build for a platform other than the current machine:

```
node scripts/publish-server.mjs --rid=osx-arm64
npx tauri build --target aarch64-apple-darwin
```

`--rid` accepts `win-x64`, `osx-arm64`, `osx-x64` or `linux-x64`, and defaults to whatever the
current machine is.

The version stamped everywhere - the server's own assembly version, and (via `sync-version.mjs`,
above) `tauri.conf.json`'s `version` field shown in the installer and the bundle's metadata - comes
from `NektoTranslate.Server/Directory.Build.props`. Bump that one value for a release; nothing here
needs editing by hand. `NEKTO_BUILD_VERSION` overrides it for a single build without touching the
committed files - CI does not use it for that reason, and normally nothing should.

Passing a version straight through, as `npm run build -- -p:Version=1.2.3`, does **not** reach the
server publish: npm appends trailing arguments to the end of the whole `sync-version.mjs &&
publish-server.mjs && tauri build` command line, so they land on `tauri build` instead of any of the
first two. This is why `NEKTO_BUILD_VERSION` and `NEKTO_RID` travel through the environment instead,
the way `.github/workflows/release.yml` sets them.

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
window opens at once, on a starting page bundled into the shell (`src-tauri/frontend-stub/index.html`)
and painted behind with the client's own background for the system theme, so no frame of the
launch is white; the server is started off the UI thread, and the window is navigated to it as soon
as `/api/health` answers 200 - up to 60 seconds. On timeout, the child is killed and a native
dialog names the log file.

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

## Window chrome

The window is frameless on every platform, and there is no separate title bar of the client's own -
each screen's own 48px header (`<AppBar>`, wrapping the back button, title and whatever else that
screen already puts there) *is* the window's title bar while running inside this shell. A native bar
above it, repeating a wordmark the library screen already shows in its own header, read as two bars
saying the same thing; folding the one into the other removes the repetition instead of restyling
it. On macOS the *real* traffic lights are kept: the window is built with
`title_bar_style(TitleBarStyle::Overlay)` and `hidden_title(true)`, so the lights stay with their
native hover and click behaviour while the page's content, `AppBar` included, extends underneath
them - `AppBar` leaves a 78px inset, flush with the bar's own left edge, for them. The overlay
assumes a title bar as tall as the tallest one it has seen (38px, before this merge); now that the
bar is 48px everywhere, `traffic_light_position(LogicalPosition::new(13.0, 18.0))` repositions the
lights explicitly - x 13 is Apple's own left margin, y 18 centres the 12px lights in the new height.
The bar's content past the inset stays left-aligned, the way the rest of the platforms read it, not
centred the way a plain macOS title bar would. Windows and Linux get
`decorations(false)` outright; Windows also gets `shadow(true)`, which is what keeps the DWM drop
shadow, the rounded corners and the native resize behaviour on an undecorated window there. Linux
loses its resize border entirely once undecorated, so a small initialization script (Linux only,
alongside the fullscreen one below) turns an 8px zone along each edge and corner back into one,
driving `startResizeDragging`.

The whole contract between the shell and the client is one object, written onto the page by an
initialization script before the client's own scripts run - the same mechanism the fullscreen script
below already uses - and read exactly once, defensively, by `NektoTranslate.Client/src/desktop/shell.ts`:

```ts
window.__NEKTO_DESKTOP__ = { platform: "windows" | "macos" | "linux", version: "<crate version>" };
```

Its presence is what tells the client it is running inside this shell at all; a plain browser tab
never sees it, and `AppBar` renders no window controls when it is absent - every screen's header
looks exactly as it does in a browser tab, one word for one word, one pixel for one pixel. Every
`AppBar` carries `data-tauri-drag-region`, and double-click on it toggles maximize on every platform
through Tauri's own drag-region handling (`core:window:allow-internal-toggle-maximize`) - nothing in
the client listens for either by hand. A screen's own non-interactive bar content (titles, counts,
the language pair) is marked `data-bar-text` so a mousedown there falls through to the header
underneath rather than being swallowed by a span with nothing to do with it. The bar's height never
changes - 48px everywhere, the same as it always was in a browser - and only the window controls
after it differ per platform. On Windows they are pressed flush against the bar's own right and top
edges with no gap of any kind - the header carries no padding of its own for this reason, the slot's
content keeps its padding instead - so the window's own top-right corner pixel is part of the close
button, the way it is on every native Windows window; Linux's round buttons keep a 6px exception to
that rule on their trailing edge only, since a circle pressed exactly into the corner would read as
clipped rather than placed:

| Platform | Controls                                                          |
| -------- | ------------------------------------------------------------------ |
| Windows  | 46px-wide, full bar height, flush to the right and top edges, Segoe Fluent Icons glyphs; close hovers `#c42b1c` |
| Linux    | 26px round buttons, 8px gap, 6px trailing padding, the app's own Material Symbols icons |
| macOS    | none - the 78px inset above is where the real traffic lights sit   |

The capability file grants exactly the window and event permissions `AppBar` and `WindowControls`
call: `allow-start-dragging` and `allow-internal-toggle-maximize` for the drag region itself,
`allow-minimize` / `allow-toggle-maximize` / `allow-close` for the three buttons,
`allow-is-maximized` / `allow-is-fullscreen` for the state they render (`allow-is-maximized` paired
with `event:allow-listen` / `allow-unlisten`, which `onResized` needs under the hood), and
`allow-start-resize-dragging` for the Linux-only resize script above.

## Updater

`tauri-plugin-updater` polls `plugins.updater.endpoints` in `tauri.conf.json` - the static
`latest.json` a release attaches, at
`https://github.com/VeyDlin/NektoTranslate/releases/latest/download/latest.json` - and checks its
`signature` fields against the public key already committed there. The four commands
`src-tauri/src/updater.rs` exposes to the client (`update_check`, `update_download`,
`update_install`, `update_pending`) are the only way the page reaches it; nothing here uses the
plugin's own JS bindings, the same way `shell.ts` avoids `@tauri-apps/api`.

`update_download` writes the installer to `<data directory>/updates/<version>/<file>` and a
`pending.json` beside it as it goes, so a person who closes the *Ready* dialog with *Not now*
still sees it offered again - as a quiet "ready" banner rather than the dialog - on `update_pending`.
On the next launch, before the server is spawned, `lib.rs` re-checks a pending update that is still
newer than the version now running and installs it there and then if the endpoint still agrees;
one that is no longer newer (this build already caught up with it) is simply deleted.

**What happens on Windows after `update_install`.** The plugin's `Update::install` runs the NSIS
installer through `ShellExecuteW` in the `windows.installMode` this file sets - `"passive"`, a small
progress window with no interaction required - and this process exits immediately afterwards
(`std::process::exit(0)`, from inside the plugin itself, before `update_install` can even return).
`restart_after_install` is left at its default of `true`, which is what makes the installer relaunch
the application once it finishes (the NSIS `/R` flag) - nothing here has to detect that or call
`tauri_plugin_process` for it. On Linux the AppImage is replaced in place and the process does *not*
exit on its own, so `update_install` calls `AppHandle::request_restart()` itself once `install`
returns; on Windows that line is never reached on a successful install.

**Testing locally**, without a real release: `NEKTO_UPDATE_ENDPOINT` overrides
`plugins.updater.endpoints` for a single run, read in `settings.rs` the same way
`NEKTO_SERVER_URL`/`NEKTO_DATA_DIRECTORY` are. Build once locally with the real signing key so the
installer gets a genuine `.sig` beside it:

```
$env:TAURI_SIGNING_PRIVATE_KEY_PATH = "$env:USERPROFILE\.tauri\nektotranslate.key"
$env:TAURI_SIGNING_PRIVATE_KEY_PASSWORD = Get-Content "$env:USERPROFILE\.tauri\nektotranslate.key.password" -Raw
npm run build
"" | Out-File notes.txt
node scripts/latest-json.mjs --version=9.9.9 --artifacts=src-tauri/target/release/bundle --notes=notes.txt
```

(`latest-json.mjs` reads only whichever platform folder your own build actually produced -
`bundle/appimage` on Linux, `bundle/nsis` on Windows - so the resulting `latest.json` names only that
one platform, which is all a local dry run needs.)

then serve the folder holding that hand-made `latest.json` (`python -m http.server`, say) and point
a run at it:

```
$env:NEKTO_UPDATE_ENDPOINT = "http://127.0.0.1:8000/latest.json"
$env:NEKTO_DATA_DIRECTORY = "C:\some\scratch\folder"
```

## Next (not in this task)

- A tray icon, so closing the window does not have to mean quitting.
- Code signing on every platform - every installer here is unsigned, and Windows and macOS will
  both warn on first run.
