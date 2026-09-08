mod server;
mod settings;

use std::process::Child;
use std::sync::Mutex;

use tauri::{Manager, RunEvent, WebviewUrl, WebviewWindowBuilder};
use tauri_plugin_opener::OpenerExt;

// Injected on every page load so F11 toggles the *native* window between fullscreen and windowed,
// and Escape leaves it - the reading mode issue #21 asks for, without touching the client at all.
// It only reaches the DOM's own keydown source and the Tauri window API that `withGlobalTauri`
// exposes on this window (see capabilities/default.json for why that reaches a loopback origin at
// all) - never the page's own script, which this never loads or depends on.
//
// The dispatched `nekto:fullscreen` event is the other half of that contract: it lets AppBar.vue
// hide its window controls the moment fullscreen changes, whichever side triggered it (this key
// handler, or the OS's own fullscreen shortcut), without polling `isFullscreen()`.
const FULLSCREEN_SCRIPT: &str = r#"
(function () {
  if (window.__NEKTO_FULLSCREEN_READY__) { return; }
  window.__NEKTO_FULLSCREEN_READY__ = true;

  var fullscreen = false;

  function apply(next) {
    fullscreen = next;
    var tauriWindow = window.__TAURI__ && window.__TAURI__.window;
    if (tauriWindow) {
      tauriWindow.getCurrentWindow().setFullscreen(next);
    }
    window.dispatchEvent(new CustomEvent('nekto:fullscreen', { detail: { fullscreen: next } }));
  }

  window.addEventListener('keydown', function (event) {
    if (event.key === 'F11') {
      event.preventDefault();
      apply(!fullscreen);
    } else if (event.key === 'Escape' && fullscreen) {
      apply(false);
    }
  });
})();
"#;

// Linux only: `.decorations(false)` leaves no window manager resize border at all there, unlike
// Windows, which still resizes an undecorated window natively (see the `decorations` call in
// `run` below). This turns an 8px zone along each edge and corner back into one, driving the same
// `startResizeDragging` the OS border would otherwise call.
//
// Registered on the capture phase so it runs before the window plugin's own drag-region listener
// (the one `data-tauri-drag-region` on the bar wires up) reaches document in the bubble phase, and
// can claim the edge pixels even where the two zones overlap - the top corners, mainly, which sit
// inside both the resize band and the bar.
#[cfg(target_os = "linux")]
const LINUX_RESIZE_SCRIPT: &str = r#"
(function () {
  if (window.__NEKTO_RESIZE_READY__) { return; }
  window.__NEKTO_RESIZE_READY__ = true;

  var EDGE_PX = 8;

  function directionFor(x, y, width, height) {
    var north = y <= EDGE_PX;
    var south = y >= height - EDGE_PX;
    var west = x <= EDGE_PX;
    var east = x >= width - EDGE_PX;

    if (north && west) { return 'NorthWest'; }
    if (north && east) { return 'NorthEast'; }
    if (south && west) { return 'SouthWest'; }
    if (south && east) { return 'SouthEast'; }
    if (north) { return 'North'; }
    if (south) { return 'South'; }
    if (west) { return 'West'; }
    if (east) { return 'East'; }

    return null;
  }

  document.addEventListener('mousedown', function (event) {
    if (event.button !== 0) { return; }

    var direction = directionFor(event.clientX, event.clientY, window.innerWidth, window.innerHeight);
    if (direction === null) { return; }

    var tauriWindow = window.__TAURI__ && window.__TAURI__.window;
    if (!tauriWindow) { return; }

    event.preventDefault();
    event.stopPropagation();
    tauriWindow.getCurrentWindow().startResizeDragging(direction);
  }, true);
})();
"#;

// The whole contract between the shell and the client's own window chrome (AppBar.vue and
// WindowControls.vue, folded into every screen's own header): which platform this is, so the
// client can pick a control shape and, on macOS, leave room for the real traffic lights, and the
// shell's own crate version, shown nowhere yet but kept here rather than added later behind its
// own round trip. Nothing else belongs on this object - nothing here proxies application settings
// or anything the client can already reach through the server it is talking to anyway.
fn desktop_script() -> String {
    let platform = if cfg!(target_os = "macos") {
        "macos"
    } else if cfg!(target_os = "linux") {
        "linux"
    } else {
        "windows"
    };

    format!(
        r#"
(function () {{
  window.__NEKTO_DESKTOP__ = Object.freeze({{ platform: "{platform}", version: "{version}" }});
}})();
"#,
        platform = platform,
        version = env!("CARGO_PKG_VERSION"),
    )
}

// The spawned server's child process, if this run spawned one. A Mutex<Option<..>> rather than a
// plain field on some struct we would have to thread through every exit path by hand - Tauri state
// is reachable from the RunEvent handler below no matter which of those paths gets taken.
struct ServerState(Mutex<Option<Child>>);

impl ServerState {
    // Idempotent on purpose: ExitRequested and Exit can both fire, and a window close might have
    // already run this once too, so a second call has to be a harmless no-op rather than a double
    // kill.
    fn kill(&self) {
        let Ok(mut guard) = self.0.lock() else {
            return;
        };

        if let Some(mut child) = guard.take() {
            let _ = child.kill();
            let _ = child.wait();
        }
    }
}

// Where the window may navigate on its own: the bundled starting page always, and the server's
// origin once one is known. Stored as the origin's ASCII serialisation rather than the origin
// itself so nothing here has to name a type from the url crate this crate does not depend on
// directly.
struct NavigationGate(Mutex<Option<String>>);

impl NavigationGate {
    fn open(&self, url: &tauri::Url) {
        if let Ok(mut guard) = self.0.lock() {
            *guard = Some(url.origin().ascii_serialization());
        }
    }

    // The starting page is served from Tauri's own origin - `tauri://localhost` on macOS and
    // Linux, `http://tauri.localhost` on Windows - and is allowed unconditionally; anything else
    // has to match the server this window was pointed at.
    fn allows(&self, url: &tauri::Url) -> bool {
        if url.scheme() == "tauri" || url.host_str() == Some("tauri.localhost") {
            return true;
        }

        let Ok(guard) = self.0.lock() else {
            return false;
        };

        guard.as_deref() == Some(url.origin().ascii_serialization().as_str())
    }
}

// The client's own page background for each theme (--ui-bg in src/assets/css/tailwind.css), so the
// colour behind the webview is the colour the page paints and no frame in between is white.
fn background_for(theme: tauri::Theme) -> tauri::window::Color {
    match theme {
        tauri::Theme::Light => tauri::window::Color(0xe9, 0xe9, 0xe9, 0xff),
        _ => tauri::window::Color(0x3b, 0x3a, 0x3a, 0xff),
    }
}

pub fn run() {
    let app = tauri::Builder::default()
        // Must be registered first - a second launch's arguments arrive here instead of starting
        // a second window, or a second spawned server for the first one's watchdog to fight with.
        .plugin(tauri_plugin_single_instance::init(|app, _args, _cwd| {
            let Some(window) = app.get_webview_window("main") else {
                return;
            };

            let _ = window.unminimize();
            let _ = window.show();
            let _ = window.set_focus();
        }))
        // Decorations are excluded from what this plugin restores: they are the per-platform
        // window chrome's to own now, decided fresh in `setup` below every launch, and the plugin
        // otherwise reapplies whatever was saved to disk on a previous run - including a `true`
        // saved before this feature existed - which silently re-decorated the window after this
        // crate had already built it frameless.
        .plugin(
            tauri_plugin_window_state::Builder::default()
                .with_state_flags(
                    tauri_plugin_window_state::StateFlags::all()
                        .difference(tauri_plugin_window_state::StateFlags::DECORATIONS),
                )
                .build(),
        )
        .plugin(tauri_plugin_opener::init())
        .manage(ServerState(Mutex::new(None)))
        .manage(NavigationGate(Mutex::new(None)))
        .setup(|app| {
            let mode = settings::resolve();
            let app_handle = app.handle().clone();

            // The window opens on the bundled starting page at once, before the server is even
            // spawned, and is navigated to the server below once it answers. Starting the server
            // first and only then creating the window - the previous order - left a second or two
            // with no window at all, followed by a white webview while the real page loaded.
            let builder = WebviewWindowBuilder::new(app, "main", WebviewUrl::App("index.html".into()))
                .title("NektoTranslate")
                .inner_size(1280.0, 860.0)
                .min_inner_size(900.0, 600.0)
                // Hidden until the background colour below is set from the window's own theme -
                // the one frame between creation and the first paint is where the white came from.
                .visible(false)
                // WebView2 draws the classic, always-visible Windows scrollbars unless told
                // otherwise, while every Chromium browser on Windows 11 has moved to the thin
                // overlay ones - and a page of prose shows the difference at once. The enable
                // flags are WebView2's own opt-in for that overlay style; the disable list is
                // what wry passes by default and has to be repeated here, because any argument
                // given replaces that default set rather than adding to it. No-op elsewhere.
                .additional_browser_args(
                    "--disable-features=msWebOOUI,msPdfOOUI,msSmartScreenProtection \
                     --enable-features=msOverlayScrollbarWinStyle,msOverlayScrollbarWinStyleAnimation",
                )
                .initialization_script(desktop_script())
                .initialization_script(FULLSCREEN_SCRIPT);

            // Frameless everywhere, so the client can draw its own bar (issue #21) - except macOS,
            // where the *real* traffic lights are kept: an overlay title bar with the title hidden
            // leaves them in place, with their native hover/click behaviour, while still letting the
            // page's content extend underneath the bar the client draws. The client's bar is 48px on
            // every platform now rather than the 38px this overlay used to assume, so the lights need
            // repositioning to sit centred in that height rather than hugging its old, shorter top:
            // x 13 keeps Apple's own left margin, y 18 centres the 12px lights in 48px.
            #[cfg(target_os = "macos")]
            let builder = builder
                .decorations(true)
                .title_bar_style(tauri::TitleBarStyle::Overlay)
                .hidden_title(true)
                .traffic_light_position(tauri::LogicalPosition::new(13.0, 18.0));

            // Windows still resizes and shows the DWM drop shadow and rounded corners on an
            // undecorated window natively - `shadow(true)` is what keeps that once `decorations`
            // turns the native frame off.
            #[cfg(target_os = "windows")]
            let builder = builder.decorations(false).shadow(true);

            // Unlike Windows, an undecorated window on Linux loses its resize border entirely, so
            // LINUX_RESIZE_SCRIPT stands in for it.
            #[cfg(target_os = "linux")]
            let builder = builder
                .decorations(false)
                .initialization_script(LINUX_RESIZE_SCRIPT);

            let gate_handle = app_handle.clone();

            let window = builder
                // Navigation is pinned to the server's own origin - SignalR, the SPA's
                // client-side routes, all of it stays in this window - plus the bundled starting
                // page the window opens on. Anything else (a link out to a source site the user
                // is importing from, say) opens in the system browser instead of taking the window
                // away from the application. The server's origin is not known when this closure is
                // built (spawn mode picks its port later), so it is read from the gate each time.
                .on_navigation(move |url| {
                    if gate_handle.state::<NavigationGate>().allows(url) {
                        return true;
                    }

                    let _ = gate_handle.opener().open_url(url.as_str(), None::<&str>);

                    false
                })
                .build()?;

            // Painted behind the page from the first frame in the theme the system reports, so
            // neither the starting page nor the hand-off to the real one ever flashes white.
            let theme = window.theme().unwrap_or(tauri::Theme::Dark);
            let _ = window.set_background_color(Some(background_for(theme)));
            let _ = window.show();

            // The server is started off the UI thread: spawn mode waits on it for up to a minute
            // (a first run's migrations, a slow disk), and the starting page above is what the
            // person sees meanwhile instead of a frozen window. server::start ends the process
            // itself, after a native dialog, when the server cannot be started.
            std::thread::spawn(move || {
                let running = server::start(&app_handle, mode);

                if let Some(child) = running.child {
                    *app_handle.state::<ServerState>().0.lock().unwrap() = Some(child);
                }

                let Ok(url) = running.url.parse::<tauri::Url>() else {
                    return;
                };

                app_handle.state::<NavigationGate>().open(&url);

                if let Some(window) = app_handle.get_webview_window("main") {
                    let _ = window.navigate(url);
                }
            });

            Ok(())
        })
        .build(tauri::generate_context!())
        .expect("error while building the tauri application");

    // The one place every exit path meets, whether the window was closed, the last window closed
    // itself, or the process is quitting for some other reason - the child's own watchdog
    // (--ParentPid) is what covers the case that never reaches even this.
    app.run(|app_handle, event| {
        if matches!(event, RunEvent::ExitRequested { .. } | RunEvent::Exit) {
            app_handle.state::<ServerState>().kill();
        }
    });
}
