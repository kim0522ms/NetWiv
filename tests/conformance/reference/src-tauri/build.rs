fn main() {
    let manifest = tauri_build::AppManifest::new().commands(&[
        "echo_json",
        "asset_fixture",
        "always_fails",
        "echo_bytes",
        "stream_numbers",
        "custom_default_allowed",
        "typed_request",
        "restricted",
        "conformance_record",
        "conformance_finish",
        "conformance_profile",
        "conformance_cases",
        "conformance_platform",
        "create_data_store_fixture",
    ]);
    tauri_build::try_build(tauri_build::Attributes::new().app_manifest(manifest)
        .plugin("contract", tauri_build::InlinedPlugin::new().commands(&["read_config"]))).unwrap();
}
