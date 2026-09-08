// Prevents an extra console window on Windows in release builds. Kept as the only thing in this
// file, same as every Tauri project template, so `cargo run` and the bundled binary share one entry
// point in lib.rs instead of two copies of the same setup.
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

fn main() {
    nekto_translate_desktop_lib::run();
}
