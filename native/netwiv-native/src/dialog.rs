#[cfg(not(target_os = "linux"))]
use std::path::Path;

use rfd::MessageButtons;
#[cfg(not(target_os = "linux"))]
use rfd::{FileDialog, MessageDialog, MessageDialogResult, MessageLevel};
use serde_json::Value;
#[cfg(not(target_os = "linux"))]
use serde_json::json;
#[cfg(not(target_os = "linux"))]
use tao::window::Window;

#[cfg(target_os = "linux")]
mod linux;
#[cfg(target_os = "linux")]
pub(crate) use linux::{PendingDialog, begin};

#[cfg(not(target_os = "linux"))]
pub(crate) fn invoke(command: &str, args: &Value, window: &Window) -> Result<Value, String> {
    match command {
        "plugin:dialog|open" | "plugin:dialog|save" => file_dialog(command, args, window),
        "plugin:dialog|message" => message_dialog(args, window),
        _ => Err(format!("unknown dialog command '{command}'")),
    }
}

#[cfg(not(target_os = "linux"))]
fn file_dialog(command: &str, args: &Value, window: &Window) -> Result<Value, String> {
    let options = args.get("options").ok_or("missing dialog options")?;
    let mut dialog = FileDialog::new().set_parent(window);
    if let Some(title) = options.get("title").and_then(Value::as_str) {
        dialog = dialog.set_title(title);
    }
    if let Some(default) = options.get("defaultPath").and_then(Value::as_str) {
        let path = Path::new(default);
        if path.is_file() || !path.exists() {
            if let Some(parent) = path.parent() {
                if parent.components().next().is_some() {
                    dialog = dialog.set_directory(parent);
                }
            }
            if let Some(name) = path.file_name() {
                dialog = dialog.set_file_name(name.to_string_lossy());
            }
        } else {
            dialog = dialog.set_directory(path);
        }
    }
    if let Some(filters) = options.get("filters") {
        let filters = filters.as_array().ok_or("dialog filters must be an array")?;
        for filter in filters {
            let name = filter.get("name").and_then(Value::as_str).ok_or("dialog filter name is missing")?;
            let extensions = filter.get("extensions").and_then(Value::as_array).ok_or("dialog filter extensions are missing")?
                .iter().map(|extension| extension.as_str().ok_or("dialog filter extension must be a string"))
                .collect::<Result<Vec<_>, _>>()?;
            dialog = dialog.add_filter(name, &extensions);
        }
    }
    if command == "plugin:dialog|save" {
        return Ok(json!(dialog.save_file().map(|path| path.to_string_lossy().into_owned())));
    }
    let directory = options.get("directory").and_then(Value::as_bool).unwrap_or(false);
    let multiple = options.get("multiple").and_then(Value::as_bool).unwrap_or(false);
    Ok(match (directory, multiple) {
        (false, false) => json!(dialog.pick_file().map(|path| path.to_string_lossy().into_owned())),
        (false, true) => json!(dialog.pick_files().map(|paths| paths.into_iter().map(|path| path.to_string_lossy().into_owned()).collect::<Vec<_>>())),
        (true, false) => json!(dialog.pick_folder().map(|path| path.to_string_lossy().into_owned())),
        (true, true) => json!(dialog.pick_folders().map(|paths| paths.into_iter().map(|path| path.to_string_lossy().into_owned()).collect::<Vec<_>>())),
    })
}

#[cfg(not(target_os = "linux"))]
fn message_dialog(args: &Value, window: &Window) -> Result<Value, String> {
    let message = args.get("message").and_then(Value::as_str).ok_or("dialog message is missing")?;
    let mut dialog = MessageDialog::new().set_parent(window).set_description(message);
    if let Some(title) = args.get("title").and_then(Value::as_str) {
        dialog = dialog.set_title(title);
    }
    let level = match args.get("kind").and_then(Value::as_str).unwrap_or("info") {
        "info" => MessageLevel::Info,
        "warning" => MessageLevel::Warning,
        "error" => MessageLevel::Error,
        other => return Err(format!("unknown dialog kind '{other}'")),
    };
    dialog = dialog.set_level(level).set_buttons(parse_buttons(args.get("buttons"))?);
    let result = match dialog.show() {
        MessageDialogResult::Yes => "Yes".to_string(),
        MessageDialogResult::No => "No".to_string(),
        MessageDialogResult::Ok => "Ok".to_string(),
        MessageDialogResult::Cancel => "Cancel".to_string(),
        MessageDialogResult::Custom(label) => label,
    };
    Ok(json!(result))
}

fn parse_buttons(value: Option<&Value>) -> Result<MessageButtons, String> {
    let Some(value) = value.filter(|value| !value.is_null()) else { return Ok(MessageButtons::Ok) };
    if let Some(name) = value.as_str() {
        return match name {
            "Ok" => Ok(MessageButtons::Ok),
            "OkCancel" => Ok(MessageButtons::OkCancel),
            "YesNo" => Ok(MessageButtons::YesNo),
            "YesNoCancel" => Ok(MessageButtons::YesNoCancel),
            _ => Err(format!("unknown dialog buttons '{name}'")),
        };
    }
    let object = value.as_object().ok_or("dialog buttons must be a string or object")?;
    if let Some(label) = object.get("OkCustom").and_then(Value::as_str) {
        return Ok(MessageButtons::OkCustom(label.to_string()));
    }
    if let Some(labels) = object.get("OkCancelCustom").and_then(Value::as_array) {
        if labels.len() == 2 {
            if let (Some(ok), Some(cancel)) = (labels[0].as_str(), labels[1].as_str()) {
                return Ok(MessageButtons::OkCancelCustom(ok.to_string(), cancel.to_string()));
            }
        }
    }
    if let Some(labels) = object.get("YesNoCancelCustom").and_then(Value::as_array) {
        if labels.len() == 3 {
            if let (Some(yes), Some(no), Some(cancel)) = (labels[0].as_str(), labels[1].as_str(), labels[2].as_str()) {
                return Ok(MessageButtons::YesNoCancelCustom(yes.to_string(), no.to_string(), cancel.to_string()));
            }
        }
    }
    Err("invalid custom dialog buttons".to_string())
}
