// Directory selection and resource path mapping adapted from Tauri 2.11.5
// src/path/{desktop.rs,mod.rs}. Copyright 2019-2024 Tauri Programme within
// The Commons Conservancy. SPDX-License-Identifier: MIT OR Apache-2.0
use std::path::{Component, Path, PathBuf};

use serde_json::Value;

pub(crate) fn resolve(args: &Value) -> Result<Value, String> {
    let directory = args["directory"].as_u64().ok_or("invalid base directory")?;
    let identifier = args["identifier"]
        .as_str()
        .ok_or("missing application identifier")?;
    let mut base = match directory {
        1 => dirs::audio_dir(),
        2 => dirs::cache_dir(),
        3 => dirs::config_dir(),
        4 => dirs::data_dir(),
        5 => dirs::data_local_dir(),
        6 => dirs::document_dir(),
        7 => dirs::download_dir(),
        8 => dirs::picture_dir(),
        9 => dirs::public_dir(),
        10 => dirs::video_dir(),
        11 => args["resourceDirectory"].as_str().map(PathBuf::from),
        12 => Some(std::env::temp_dir()),
        13 => dirs::config_dir().map(|p| p.join(identifier)),
        14 => dirs::data_dir().map(|p| p.join(identifier)),
        15 => dirs::data_local_dir().map(|p| p.join(identifier)),
        16 => dirs::cache_dir().map(|p| p.join(identifier)),
        17 => app_log_dir(identifier),
        18 => dirs::desktop_dir(),
        19 => dirs::executable_dir(),
        20 => dirs::font_dir(),
        21 => dirs::home_dir(),
        22 => dirs::runtime_dir(),
        23 => dirs::template_dir(),
        _ => return Err("invalid base directory".into()),
    }
    .ok_or("unknown path")?;
    if let Some(path) = args["path"].as_str() {
        if directory == 11 {
            base.push(resource_path(Path::new(path)));
        } else {
            base.push(path);
        }
    }
    Ok(Value::String(base.to_string_lossy().into_owned()))
}

fn app_log_dir(identifier: &str) -> Option<PathBuf> {
    #[cfg(target_os = "macos")]
    { dirs::home_dir().map(|p| p.join("Library/Logs").join(identifier)) }
    #[cfg(not(target_os = "macos"))]
    { dirs::data_local_dir().map(|p| p.join(identifier).join("logs")) }
}

fn resource_path(path: &Path) -> PathBuf {
    let mut result = PathBuf::new();
    for component in path.components() {
        match component {
            Component::Prefix(_) | Component::CurDir => {}
            Component::RootDir => result.push("_root_"),
            Component::ParentDir => result.push("_up_"),
            Component::Normal(value) => result.push(value),
        }
    }
    result
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn resource_parent_and_root_components_are_encoded() {
        assert_eq!(
            resource_path(Path::new("../foo/../bar")),
            PathBuf::from("_up_/foo/_up_/bar")
        );
        assert_eq!(
            resource_path(Path::new("/foo/./bar")),
            PathBuf::from("_root_/foo/bar")
        );
    }

    #[test]
    fn app_logs_use_local_data_and_preserve_relative_components() {
        let args = json!({"directory":17,"identifier":"dev.netwiv.paths","path":"a/../b"});
        #[cfg(not(target_os = "macos"))]
        let expected = dirs::data_local_dir()
            .unwrap()
            .join("dev.netwiv.paths")
            .join("logs")
            .join("a/../b");
        #[cfg(target_os = "macos")]
        let expected = dirs::home_dir().unwrap().join("Library/Logs/dev.netwiv.paths/a/../b");
        assert_eq!(
            resolve(&args).unwrap(),
            Value::String(expected.to_string_lossy().into_owned())
        );
    }

    #[cfg(target_os = "windows")]
    #[test]
    fn unsupported_windows_directories_return_unknown_path() {
        for directory in [19, 20, 22] {
            assert_eq!(
                resolve(&json!({"directory":directory,"identifier":"test"})),
                Err("unknown path".into())
            );
        }
    }
}
