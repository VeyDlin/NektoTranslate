// The four commands the client's `src/desktop/updates.ts` calls to drive an update from the moment
// a new release exists to the moment the person is running it: `update_check` asks the endpoint
// (through the plugin, against `latest.json`), `update_download` fetches the installer and reports
// progress, `update_install` verifies and runs it, and `update_pending` lets a later launch recover
// a download an earlier one made but never installed. Everything the client needs remembered
// between launches - which version, which file, when - lives on disk under
// `<data directory>/updates/`, not in this process's memory, so a launch that never called
// `update_download` still finds what an earlier one left behind (see `pending_newer_version` and
// `reconcile_pending_on_startup`, called from lib.rs's `setup` before the server is spawned).
use std::fs;
use std::path::{Path, PathBuf};

use serde::{Deserialize, Serialize};
use tauri::{AppHandle, Emitter, Manager};
use tauri_plugin_updater::UpdaterExt;
use time::format_description::well_known::Rfc3339;
use time::OffsetDateTime;

use crate::{DataDirectory, ServerState};

// What the client's UpdateBanner needs to know a new version exists. The plugin's own `Update`
// carries far more - the download URL, the signature, the raw JSON - than a page ever has to see.
#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct UpdateAvailable {
    version: String,
    notes: Option<String>,
    date: Option<String>,
}

// Written to `<data directory>/updates/<version>/pending.json` once a download finishes, and read
// back on every later launch until it is installed or found stale. `file` names the installer
// inside that same directory rather than a full path, so the whole `updates/` tree could move (a
// fresh install pointed at a different data directory, say) without carrying a baked-in path.
#[derive(Serialize, Deserialize, Clone)]
#[serde(rename_all = "camelCase")]
pub struct PendingUpdate {
    version: String,
    file: String,
    downloaded_at: String,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DownloadResult {
    version: String,
    file: String,
}

// `chunk_length` from the plugin's own `on_chunk` callback is the size of *this* chunk, not a
// running total (see the JS `DownloadEvent::Progress` this mirrors) - `update_download` below adds
// each one to a running `downloaded` before this goes out, so the client never has to.
#[derive(Clone, Serialize)]
struct DownloadProgress {
    downloaded: u64,
    total: Option<u64>,
}

enum InstallOutcome {
    Installed,
    VersionsDiffer,
}

fn updates_dir(data_directory: &str) -> PathBuf {
    Path::new(data_directory).join("updates")
}

fn version_dir(data_directory: &str, version: &str) -> PathBuf {
    updates_dir(data_directory).join(version)
}

fn pending_path(directory: &Path) -> PathBuf {
    directory.join("pending.json")
}

fn read_pending(directory: &Path) -> Option<PendingUpdate> {
    let contents = fs::read_to_string(pending_path(directory)).ok()?;

    serde_json::from_str(&contents).ok()
}

// At most one update is ever downloaded at a time - update_download below clears the whole
// `updates/` tree before writing a fresh one, and a client already showing "ready" has nothing left
// to offer *Update* for - so this only ever has one version directory to find. Scanning for it
// rather than remembering a path in some other file is what lets a launch with no memory of the one
// before it (lib.rs's "next launch" case) find it all the same.
fn find_pending(data_directory: &str) -> Option<(PathBuf, PendingUpdate)> {
    let entries = fs::read_dir(updates_dir(data_directory)).ok()?;

    for entry in entries.flatten() {
        let path = entry.path();

        if !path.is_dir() {
            continue;
        }

        if let Some(pending) = read_pending(&path) {
            return Some((path, pending));
        }
    }

    None
}

fn write_pending(directory: &Path, pending: &PendingUpdate) -> std::io::Result<()> {
    let json = serde_json::to_string_pretty(pending).unwrap_or_else(|_| "{}".to_string());

    fs::write(pending_path(directory), json)
}

fn now_iso() -> String {
    OffsetDateTime::now_utc()
        .format(&Rfc3339)
        .unwrap_or_default()
}

// The installer's own file name, read off the end of the download URL rather than invented here -
// latest-json.mjs names it after the real bundle output, and carrying that name through is what
// lets someone who goes looking in `updates/<version>/` recognise the file.
fn file_name_from_url(url: &tauri::Url) -> String {
    url.path_segments()
        .and_then(|mut segments| segments.next_back())
        .filter(|name| !name.is_empty())
        .unwrap_or("update")
        .to_string()
}

fn current_version() -> &'static str {
    env!("CARGO_PKG_VERSION")
}

// Compares only the three numeric components `latest-json.mjs` and Directory.Build.props ever
// write - a build metadata suffix, if one is ever added, plays no part in whether an update is
// offered. Anything that fails to parse is never treated as newer: a malformed `pending.json` should
// be forgotten, not installed.
fn parse_version(value: &str) -> Option<(u64, u64, u64)> {
    let mut parts = value.trim().split('.');
    let major = parts.next()?.parse().ok()?;
    let minor = parts.next().unwrap_or("0").parse().ok()?;
    let patch = parts.next().unwrap_or("0").parse().ok()?;

    Some((major, minor, patch))
}

fn is_newer(candidate: &str, than: &str) -> bool {
    match (parse_version(candidate), parse_version(than)) {
        (Some(candidate), Some(than)) => candidate > than,
        _ => false,
    }
}

// Read-only, and purely local: whether `pending.json` names a version newer than the one now
// running, answered before the window is even created so the starting page can already say
// "Updating to <version>…" instead of "Starting…" the moment a pending update exists, without
// waiting on the network check that decides whether it can actually be applied.
pub fn pending_newer_version(data_directory: &str) -> Option<String> {
    let (_, pending) = find_pending(data_directory)?;

    is_newer(&pending.version, current_version()).then_some(pending.version)
}

async fn build_updater(app: &AppHandle) -> Result<tauri_plugin_updater::Updater, String> {
    let mut builder = app.updater_builder();

    // Overrides the release's own `plugins.updater.endpoints` for one run, so the dry run described
    // in NektoTranslate.Desktop/README.md can point a locally built shell at a
    // `python -m http.server` serving a hand-made `latest.json` instead of the real GitHub release.
    if let Some(endpoint) = crate::settings::update_endpoint() {
        let url: tauri::Url = endpoint
            .parse()
            .map_err(|error| format!("NEKTO_UPDATE_ENDPOINT is not a URL ({endpoint}): {error}"))?;

        builder = builder
            .endpoints(vec![url])
            .map_err(|error| error.to_string())?;
    }

    builder.build().map_err(|error| error.to_string())
}

#[tauri::command]
pub async fn update_check(app: AppHandle) -> Option<UpdateAvailable> {
    let updater = match build_updater(&app).await {
        Ok(updater) => updater,
        Err(error) => {
            eprintln!("update check: could not configure the updater: {error}");

            return None;
        }
    };

    match updater.check().await {
        Ok(Some(update)) => Some(UpdateAvailable {
            version: update.version.clone(),
            notes: update.body.clone(),
            date: update
                .date
                .map(|date| date.format(&Rfc3339).unwrap_or_default()),
        }),
        Ok(None) => None,
        Err(error) => {
            eprintln!("update check failed: {error}");

            None
        }
    }
}

#[tauri::command]
pub async fn update_download(app: AppHandle) -> Result<DownloadResult, String> {
    let updater = build_updater(&app).await?;

    let update = updater
        .check()
        .await
        .map_err(|error| error.to_string())?
        .ok_or_else(|| "No update is available to download".to_string())?;

    let data_directory = app.state::<DataDirectory>().0.clone();
    let version = update.version.clone();
    let window = app.get_webview_window("main");
    let mut downloaded: u64 = 0;

    let bytes = update
        .download(
            move |chunk_length, total| {
                downloaded += chunk_length as u64;

                if let Some(window) = &window {
                    let _ = window.emit(
                        "nekto:update-progress",
                        DownloadProgress { downloaded, total },
                    );
                }
            },
            || {},
        )
        .await
        .map_err(|error| error.to_string())?;

    // A stale download for some other version is not worth keeping once a fresh one exists - the
    // client only ever offers *Update* once at a time, so there is never a reason to hold two.
    let _ = fs::remove_dir_all(updates_dir(&data_directory));

    let directory = version_dir(&data_directory, &version);

    fs::create_dir_all(&directory).map_err(|error| error.to_string())?;

    let file = file_name_from_url(&update.download_url);

    fs::write(directory.join(&file), &bytes).map_err(|error| error.to_string())?;

    let pending = PendingUpdate {
        version: version.clone(),
        file: file.clone(),
        downloaded_at: now_iso(),
    };

    write_pending(&directory, &pending).map_err(|error| error.to_string())?;

    Ok(DownloadResult { version, file })
}

// Shared by the explicit `update_install` command and lib.rs's own "next launch" reconcile: re-checks
// against the endpoint rather than trusting whatever the download happened to be signed with -
// `Update::install` needs a live `Update` (it is what carries the signature to verify the bytes
// against), and the moment this runs is not always the moment the bytes were fetched. The "next
// launch" path in particular has no `Update` left in memory at all - the process that downloaded it
// is not this one.
async fn reinstall(
    app: &AppHandle,
    directory: &Path,
    pending: &PendingUpdate,
) -> Result<InstallOutcome, String> {
    let updater = build_updater(app).await?;
    let update = updater.check().await.map_err(|error| error.to_string())?;

    let Some(update) = update else {
        return Ok(InstallOutcome::VersionsDiffer);
    };

    if update.version != pending.version {
        return Ok(InstallOutcome::VersionsDiffer);
    }

    let bytes = fs::read(directory.join(&pending.file)).map_err(|error| error.to_string())?;

    // The installer must never have to fight a running NektoTranslate.Api.exe for its own files.
    app.state::<ServerState>().kill();

    update.install(&bytes).map_err(|error| error.to_string())?;

    // Only reached on macOS/Linux: a successful install on Windows already exited this process from
    // inside `install` above, having launched the NSIS installer in passive mode (see
    // NektoTranslate.Desktop/README.md for what that installer does next). request_restart here
    // covers the platforms where `install` returns instead of exiting - the AppImage was replaced in
    // place, and nothing short of a restart runs the new bytes.
    app.request_restart();

    Ok(InstallOutcome::Installed)
}

#[tauri::command]
pub async fn update_install(app: AppHandle) -> Result<(), String> {
    let data_directory = app.state::<DataDirectory>().0.clone();

    let (directory, pending) =
        find_pending(&data_directory).ok_or_else(|| "No update is ready to install".to_string())?;

    match reinstall(&app, &directory, &pending).await? {
        InstallOutcome::Installed => Ok(()),
        InstallOutcome::VersionsDiffer => Err(
            "The downloaded update no longer matches the latest release - try Update again."
                .to_string(),
        ),
    }
}

#[tauri::command]
pub fn update_pending(app: AppHandle) -> Option<PendingUpdate> {
    let data_directory = app.state::<DataDirectory>().0.clone();

    find_pending(&data_directory).map(|(_, pending)| pending)
}

// Called once, off the UI thread, before the server itself is spawned - see lib.rs's `setup`. A
// pending update that already applied (this process's own version caught up with it, or overtook
// it) is simply forgotten; one still ahead is re-verified and installed only if the endpoint still
// agrees it is current. Returns whether this run is now installing/restarting rather than starting
// normally, so lib.rs knows to skip spawning the server it would otherwise replace.
pub async fn reconcile_pending_on_startup(app: &AppHandle, data_directory: &str) -> bool {
    let Some((directory, pending)) = find_pending(data_directory) else {
        return false;
    };

    if !is_newer(&pending.version, current_version()) {
        let _ = fs::remove_dir_all(updates_dir(data_directory));

        return false;
    }

    match reinstall(app, &directory, &pending).await {
        Ok(InstallOutcome::Installed) => true,
        Ok(InstallOutcome::VersionsDiffer) => {
            eprintln!(
                "pending update {} no longer matches what the release feed offers - leaving it for the next launch",
                pending.version
            );

            false
        }
        Err(error) => {
            eprintln!(
                "could not apply the pending update {}: {error}",
                pending.version
            );

            false
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn is_newer_compares_the_three_numeric_components() {
        assert!(is_newer("0.2.1", "0.2.0"));
        assert!(is_newer("0.3.0", "0.2.9"));
        assert!(is_newer("1.0.0", "0.9.9"));
        assert!(!is_newer("0.2.0", "0.2.0"));
        assert!(!is_newer("0.1.9", "0.2.0"));
    }

    #[test]
    fn is_newer_treats_an_unparsable_version_as_never_newer() {
        assert!(!is_newer("not-a-version", "0.2.0"));
        assert!(!is_newer("0.2.1", "also-not-a-version"));
    }

    #[test]
    fn parse_version_defaults_missing_components_to_zero() {
        assert_eq!(parse_version("2"), Some((2, 0, 0)));
        assert_eq!(parse_version("2.5"), Some((2, 5, 0)));
        assert_eq!(parse_version("2.5.9"), Some((2, 5, 9)));
    }

    #[test]
    fn file_name_from_url_takes_the_last_path_segment() {
        let url: tauri::Url = "https://github.com/VeyDlin/NektoTranslate/releases/download/v0.2.1/NektoTranslate_0.2.1_x64-setup.exe"
            .parse()
            .unwrap();

        assert_eq!(
            file_name_from_url(&url),
            "NektoTranslate_0.2.1_x64-setup.exe"
        );
    }
}
