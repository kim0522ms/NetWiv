use std::path::PathBuf;

use serde::Deserialize;
use serde_json::Value;

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub(crate) struct RunConfig {
    pub initialization_script: String,
    pub windows: Vec<WindowConfig>,
    #[serde(default)]
    pub default_window_icon: Option<Value>,
    #[serde(default)]
    pub local_data_directory: Option<PathBuf>,
    #[serde(default)]
    pub app_local_data_directory: Option<PathBuf>,
    #[serde(default)]
    pub asset_protocol_enabled: bool,
    #[serde(default)]
    pub isolation_protocol: Option<String>,
    #[serde(default)]
    pub ipc_protocol_enabled: bool,
    #[serde(default)]
    pub uri_scheme_protocols: Vec<String>,
    #[serde(default)]
    pub track_webview_parents: bool,
}

#[derive(Clone, Debug, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub(crate) struct WindowConfig {
    pub label: String,
    pub title: String,
    pub url: String,
    pub width: f64,
    pub height: f64,
    #[serde(default)]
    pub extra_config: Value,
    pub webviews: Option<Vec<WebviewConfig>>,
}

#[derive(Clone, Debug, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub(crate) struct WebviewConfig {
    pub label: String,
    pub url: String,
    #[serde(default)]
    pub x: f64,
    #[serde(default)]
    pub y: f64,
    pub width: f64,
    pub height: f64,
    #[serde(default)]
    pub extra_config: Value,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub(crate) struct Submission {
    pub command: String,
    pub args: Value,
    pub webview_label: String,
    pub window_label: Option<String>,
    pub source: String,
    pub is_authorized: bool,
    #[serde(default)]
    pub is_backend: bool,
    #[serde(default)]
    pub document_id: u64,
    #[serde(default)]
    pub scope: Value,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub(crate) struct AssetResponseMetadata {
    pub status_code: u16,
    pub content_type: String,
    #[serde(default)]
    pub headers: std::collections::HashMap<String, String>,
}

pub(crate) fn validate_config(config: &RunConfig) -> Result<(), String> {
    if let Some(protocol) = &config.isolation_protocol
        && (!protocol.starts_with("isolation-")
            || protocol.len() <= "isolation-".len()
            || !protocol
                .bytes()
                .all(|byte| byte.is_ascii_lowercase() || byte.is_ascii_digit() || byte == b'-'))
    {
        return Err("isolationProtocol must be a lower-case isolation- prefixed URI scheme".into());
    }
    let mut window_labels = std::collections::HashSet::new();
    let mut webview_labels = std::collections::HashSet::new();
    for window in &config.windows {
        validate_label(&window.label, "window")?;
        if !window_labels.insert(window.label.as_str()) {
            return Err(format!("duplicate native window label '{}'", window.label));
        }
        validate_dimension(window.width, "window width")?;
        validate_dimension(window.height, "window height")?;
        if let Some(webviews) = &window.webviews {
            for webview in webviews {
                validate_label(&webview.label, "webview")?;
                if !webview_labels.insert(webview.label.as_str()) {
                    return Err(format!(
                        "duplicate native webview label '{}'",
                        webview.label
                    ));
                }
                validate_dimension(webview.width, "webview width")?;
                validate_dimension(webview.height, "webview height")?;
                if !webview.x.is_finite() || !webview.y.is_finite() {
                    return Err(format!(
                        "webview '{}' has a non-finite position",
                        webview.label
                    ));
                }
            }
        } else if !webview_labels.insert(window.label.as_str()) {
            return Err(format!(
                "duplicate implicit native webview label '{}'",
                window.label
            ));
        }
    }
    Ok(())
}

fn validate_label(label: &str, _kind: &str) -> Result<(), String> {
    if label
        .chars()
        .all(|character| character.is_alphanumeric() || "-/:_".contains(character))
    {
        Ok(())
    } else {
        Err("runtime error: Window labels must only include alphanumeric characters, `-`, `/`, `:` and `_`."
            .to_string())
    }
}

pub(crate) fn validate_runtime_label(label: &str, kind: &str) -> Result<(), String> {
    validate_label(label, kind)
}

fn validate_dimension(value: f64, name: &str) -> Result<(), String> {
    if value.is_finite() && value > 0.0 && value <= u32::MAX as f64 {
        Ok(())
    } else {
        Err(format!("{name} must be a positive finite value"))
    }
}

pub(crate) fn resolve_url(url: &str, use_https_scheme: bool) -> String {
    if url.contains("://") || url.starts_with("data:") || url.starts_with("about:") {
        return url.to_owned();
    }
    let path = url.replace('\\', "/");
    let path = path.trim_start_matches('/');
    if cfg!(target_os = "windows") {
        let scheme = if use_https_scheme { "https" } else { "http" };
        format!("{scheme}://tauri.localhost/{path}")
    } else {
        format!("tauri://localhost/{path}")
    }
}

pub(crate) fn required_string<'a>(value: &'a Value, name: &str) -> Result<&'a str, String> {
    value
        .get(name)
        .and_then(Value::as_str)
        .filter(|s| !s.is_empty())
        .ok_or_else(|| format!("command argument '{name}' must be a non-empty string"))
}

pub(crate) fn bool_arg(value: &Value, name: &str) -> Result<bool, String> {
    value
        .get(name)
        .and_then(Value::as_bool)
        .ok_or_else(|| format!("command argument '{name}' must be a boolean"))
}

pub(crate) fn f64_arg(value: &Value, name: &str) -> Result<f64, String> {
    value
        .get(name)
        .and_then(Value::as_f64)
        .filter(|v| v.is_finite())
        .ok_or_else(|| format!("command argument '{name}' must be a finite number"))
}

pub(crate) fn rid_arg(value: &Value) -> Result<u32, String> {
    value
        .get("rid")
        .and_then(Value::as_u64)
        .and_then(|v| u32::try_from(v).ok())
        .ok_or_else(|| "command argument 'rid' must be a native resource id".into())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn windowless_config_still_validates_protocols() {
        let mut config: RunConfig = serde_json::from_value(serde_json::json!({
            "initializationScript": "", "windows": []
        })).unwrap();
        assert!(validate_config(&config).is_ok());
        config.isolation_protocol = Some("not-an-isolation-scheme".into());
        assert!(validate_config(&config).is_err());
    }
}
