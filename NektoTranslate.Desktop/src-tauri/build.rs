fn main() {
    // Tauri only enforces the ACL against these four commands of ours because the window is a
    // remote origin (see capabilities/default.json) - a local `tauri://` origin would run any
    // registered command unconditionally. `AppManifest::commands` is what makes the four generate
    // real `allow-<command>` permissions at all (gen/schemas/desktop-schema.json, after a build),
    // rather than the ACL simply refusing them with nothing to grant.
    tauri_build::try_build(tauri_build::Attributes::new().app_manifest(
        tauri_build::AppManifest::new().commands(&[
            "update_check",
            "update_download",
            "update_install",
            "update_pending",
        ]),
    ))
    .expect("failed to run tauri-build");
}
