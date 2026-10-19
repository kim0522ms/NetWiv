use std::{fs, path::PathBuf};
fn main() {
    let directory = PathBuf::from(std::env::var_os("CARGO_MANIFEST_DIR").unwrap());
    let assets = directory.join("web-assets");
    fs::create_dir_all(assets.join("external/tslib")).unwrap();
    for entry in fs::read_dir(directory.join("../frontend")).unwrap() {
        let entry = entry.unwrap();
        fs::write(assets.join(entry.file_name()), fs::read(entry.path()).unwrap()).unwrap();
    }
    let vendor = directory.join("vendor/@tauri-apps/api");
    fs::write(assets.join("core.js"), fs::read(vendor.join("core.js")).unwrap()).unwrap();
    fs::write(assets.join("external/tslib/tslib.es6.js"), fs::read(vendor.join("external/tslib/tslib.es6.js")).unwrap()).unwrap();
    println!("cargo:rerun-if-changed=../frontend");
    println!("cargo:rerun-if-changed=../isolation");
    tauri_build::build();
}
