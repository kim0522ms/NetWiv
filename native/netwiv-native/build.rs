fn main() {
    #[cfg(target_os = "windows")]
    embed_resource::compile("netwiv-native.rc", embed_resource::NONE)
        .manifest_required()
        .unwrap();
}
