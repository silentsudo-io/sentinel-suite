// Sentinel Azimuth — Tauri shell.
//
// The shell is deliberately THIN. It opens one window pointed at the data
// sidecar (plain Node, DuckDB) which also serves the built UI. Keeping the shell
// free of native data code is what makes the Tauri-vs-Electron choice reversible:
// nothing but this file and the Cargo manifest is shell-specific.
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

fn main() {
    tauri::Builder::default()
        .run(tauri::generate_context!())
        .expect("error while running the Sentinel Azimuth shell");
}
