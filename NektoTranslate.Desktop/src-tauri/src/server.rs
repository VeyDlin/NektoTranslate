// Locating, spawning, health-polling and stopping the server the window shows - or, in attach
// mode, just health-polling one somebody else already started.
use std::fs;
use std::net::TcpListener;
use std::path::{Path, PathBuf};
use std::process::{Child, Command, Stdio};
use std::time::{Duration, Instant};

use tauri::{AppHandle, Manager};

use crate::settings::ShellMode;

const SPAWN_HEALTH_TIMEOUT: Duration = Duration::from_secs(60);

const ATTACH_HEALTH_TIMEOUT: Duration = Duration::from_secs(30);

const HEALTH_POLL_INTERVAL: Duration = Duration::from_millis(500);

// What the window ends up showing, and the child to stop when it closes - `None` in attach mode,
// where this process never started anything and has nothing to stop.
pub struct RunningServer {
    pub url: String,
    pub child: Option<Child>,
}

pub fn start(app: &AppHandle, mode: ShellMode) -> RunningServer {
    match mode {
        ShellMode::Attach { url } => attach(&url),
        ShellMode::Spawn { data_directory } => spawn(app, data_directory),
    }
}

fn attach(url: &str) -> RunningServer {
    // A dev server proxies /api through to the real API, so /api/health usually answers there
    // too - but a developer running only `npm run dev`, with the API not started yet, still has
    // a webview worth opening, and `/` is what tells us that much is true.
    let health_url = format!("{}/api/health", trim_trailing_slash(url));
    let root_url = format!("{}/", trim_trailing_slash(url));

    let reachable = wait_for(ATTACH_HEALTH_TIMEOUT, || {
        answers_200(&health_url) || answers_200(&root_url)
    });

    if !reachable {
        fatal(&format!(
            "Could not reach {url} (tried {health_url} and {root_url} for {}s).\n\n\
            Start the server first, or point --server-url / NEKTO_SERVER_URL at one that is already running.",
            ATTACH_HEALTH_TIMEOUT.as_secs()
        ));
    }

    RunningServer {
        url: url.to_string(),
        child: None,
    }
}

fn spawn(app: &AppHandle, data_directory: Option<String>) -> RunningServer {
    let binary = server_binary_path(app);

    if !binary.exists() {
        fatal(&format!(
            "The server binary is missing:\n{}\n\n\
            Run \"npm run build\" (or \"npm run server:publish\" for a dev run) in NektoTranslate.Desktop first.",
            binary.display()
        ));
    }

    let data_directory = data_directory.unwrap_or_else(default_data_directory);
    let logs_directory = Path::new(&data_directory).join("logs");

    fs::create_dir_all(&logs_directory).unwrap_or_else(|error| {
        fatal(&format!(
            "Could not create {}: {error}",
            logs_directory.display()
        ));
    });

    let log_path = logs_directory.join("server.log");
    rotate_log(&log_path, &logs_directory.join("server.previous.log"));

    let port = free_port();
    let server_url = format!("http://127.0.0.1:{port}");

    let mut command = Command::new(&binary);

    command
        .arg(format!("--ServerUrl={server_url}"))
        .arg(format!("--DataDirectory={data_directory}"))
        .arg(format!("--ParentPid={}", std::process::id()))
        // The server resolves its own wwwroot, parsers/ and .playwright/ next to the binary, but
        // only relative to its *working* directory - without this it inherits whatever directory
        // the shell itself happened to be launched from instead, and serves nothing.
        .current_dir(binary.parent().unwrap_or_else(|| Path::new(".")))
        .stdin(Stdio::null())
        .stdout(log_sink(&log_path))
        .stderr(log_sink(&log_path));

    // Without this, spawning a console binary from a windowed (non-console) app allocates and
    // flashes a brand new console window on Windows - exactly the console the child's own
    // stdout/stderr redirection above is meant to make unnecessary.
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;

        const CREATE_NO_WINDOW: u32 = 0x0800_0000;

        command.creation_flags(CREATE_NO_WINDOW);
    }

    let mut child = command.spawn().unwrap_or_else(|error| {
        fatal(&format!(
            "Failed to start the server ({}): {error}",
            binary.display()
        ));
    });

    let health_url = format!("{server_url}/api/health");
    let healthy = wait_for(SPAWN_HEALTH_TIMEOUT, || answers_200(&health_url));

    if !healthy {
        let _ = child.kill();
        let _ = child.wait();

        fatal(&format!(
            "The server did not answer {health_url} within {}s.\nSee {} for details.",
            SPAWN_HEALTH_TIMEOUT.as_secs(),
            log_path.display()
        ));
    }

    RunningServer {
        url: server_url,
        child: Some(child),
    }
}

// The previous run's log is kept once, under its own name, rather than appended to - a fresh file
// every run is what keeps "the last thing this server did before it stopped answering" readable
// instead of buried in whatever the run before that also wrote.
fn rotate_log(current: &Path, previous: &Path) {
    if current.exists() {
        let _ = fs::rename(current, previous);
    }
}

fn log_sink(path: &Path) -> Stdio {
    match fs::File::create(path) {
        Ok(file) => Stdio::from(file),
        // A log file that could not be created is not worth failing the whole launch over - the
        // server still runs, just without a transcript on disk.
        Err(_) => Stdio::null(),
    }
}

// The server binary is a resource in a release bundle, and sits under src-tauri/server for a
// `cargo`/`tauri dev` run - the same place scripts/publish-server.mjs writes it to, since
// CARGO_MANIFEST_DIR at compile time is src-tauri itself.
fn server_binary_path(app: &AppHandle) -> PathBuf {
    let file_name = if cfg!(windows) {
        "NektoTranslate.Api.exe"
    } else {
        "NektoTranslate.Api"
    };

    if cfg!(debug_assertions) {
        return Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("server")
            .join(file_name);
    }

    app.path()
        .resolve(
            Path::new("server").join(file_name),
            tauri::path::BaseDirectory::Resource,
        )
        .unwrap_or_else(|error| {
            fatal(&format!(
                "Could not resolve the bundled resource directory: {error}"
            ))
        })
}

// Mirrors DataPaths.Resolve on the server side, so a spawned server and this shell agree on where
// the library lives even though the shell also passes --DataDirectory explicitly to make sure of
// it - this is only what that default would have been. `pub(crate)` rather than private: updater.rs
// reads the very same default so a downloaded update and the server it updates always agree on
// where the library - and now the `updates/` folder beside it - lives.
pub(crate) fn default_data_directory() -> String {
    let base = if cfg!(target_os = "windows") {
        dirs::data_local_dir()
    } else {
        dirs::data_dir()
    };

    let base = base.unwrap_or_else(|| {
        fatal("Could not determine a per-user data directory on this platform. Pass --data-directory explicitly.")
    });

    base.join("NektoTranslate").to_string_lossy().into_owned()
}

fn free_port() -> u16 {
    // Bound, read, and released rather than handed to the child directly: the child needs a
    // number to listen on, not an open socket, and TcpListener has no way to hand off the one it
    // holds. The moment between the drop below and the server's own bind is a race between this
    // process and another one taking the same port in that instant, not the case this guards.
    let listener = TcpListener::bind("127.0.0.1:0").unwrap_or_else(|error| {
        fatal(&format!("Could not bind an ephemeral port: {error}"));
    });

    listener
        .local_addr()
        .unwrap_or_else(|error| fatal(&format!("Bound listener has no local address: {error}")))
        .port()
}

fn wait_for<Probe: FnMut() -> bool>(timeout: Duration, mut probe: Probe) -> bool {
    let deadline = Instant::now() + timeout;

    loop {
        if probe() {
            return true;
        }

        if Instant::now() >= deadline {
            return false;
        }

        std::thread::sleep(HEALTH_POLL_INTERVAL);
    }
}

fn answers_200(url: &str) -> bool {
    ureq::get(url)
        .call()
        .map(|response| response.status().as_u16() == 200)
        .unwrap_or(false)
}

fn trim_trailing_slash(url: &str) -> &str {
    url.trim_end_matches('/')
}

// A native dialog, not just a console line: this runs before any window exists, so the console is
// the only other place the message could otherwise be seen at all.
fn fatal(message: &str) -> ! {
    eprintln!("{message}");

    rfd::MessageDialog::new()
        .set_title("NektoTranslate")
        .set_description(message)
        .set_level(rfd::MessageLevel::Error)
        .set_buttons(rfd::MessageButtons::Ok)
        .show();

    std::process::exit(1);
}
