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
        .plugin(tauri_plugin_window_state::Builder::default().build())
        .plugin(tauri_plugin_opener::init())
        .manage(ServerState(Mutex::new(None)))
        .setup(|app| {
            let mode = settings::resolve();
            let running = server::start(app.handle(), mode);

            if let Some(child) = running.child {
                *app.state::<ServerState>().0.lock().unwrap() = Some(child);
            }

            let base_url: tauri::Url = running
                .url
                .parse()
                .expect("the resolved server url should be a valid url");
            let origin = base_url.origin();
            let app_handle = app.handle().clone();

            WebviewWindowBuilder::new(app, "main", WebviewUrl::External(base_url))
                .title("NektoTranslate")
                .inner_size(1280.0, 860.0)
                .min_inner_size(900.0, 600.0)
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
                .initialization_script(FULLSCREEN_SCRIPT)
                // Navigation is pinned to the server's own origin - SignalR, the SPA's
                // client-side routes, all of it stays in this window. Anything else (a link out
                // to a source site the user is importing from, say) opens in the system browser
                // instead of taking the window away from the application.
                .on_navigation(move |url| {
                    if url.origin() == origin {
                        return true;
                    }

                    let _ = app_handle.opener().open_url(url.as_str(), None::<&str>);

                    false
                })
                .build()?;

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
