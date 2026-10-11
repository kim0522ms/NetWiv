use serde_json::Value;

pub(crate) fn notify(args: &Value) -> Result<Value, String> {
    let options = args
        .get("options")
        .and_then(Value::as_object)
        .ok_or("notification options must be an object")?;
    let title = options
        .get("title")
        .and_then(Value::as_str)
        .ok_or("notification title must be a string")?
        .to_owned();
    let body = optional_string(options, "body")?;
    let icon = optional_string(options, "icon")?;
    let sound = optional_string(options, "sound")?;
    let app_id = args.get("identifier").and_then(Value::as_str).map(str::to_owned);
    std::thread::spawn(move || {
        let mut notification = notify_rust::Notification::new();
        notification.summary(&title);
        if let Some(body) = body.as_deref() {
            notification.body(body);
        }
        if let Some(icon) = icon.as_deref() {
            notification.icon(icon);
        } else {
            notification.auto_icon();
        }
        if let Some(sound) = sound.as_deref() {
            notification.sound_name(sound);
        }
        #[cfg(windows)]
        if let Some(app_id) = app_id.as_deref() {
            notification.app_id(app_id);
        }
        #[cfg(not(windows))]
        let _ = app_id;
        let _ = notification.show();
    });
    Ok(Value::Null)
}

fn optional_string(options: &serde_json::Map<String, Value>, name: &str) -> Result<Option<String>, String> {
    match options.get(name) {
        None | Some(Value::Null) => Ok(None),
        Some(Value::String(value)) => Ok(Some(value.clone())),
        _ => Err(format!("notification {name} must be a string")),
    }
}

#[cfg(test)]
mod tests {
    #[test]
    fn invalid_payloads_fail_without_dispatch() {
        assert!(super::notify(&serde_json::json!({})).is_err());
        assert!(super::notify(&serde_json::json!({"options":{"title":2}})).is_err());
        assert!(super::notify(&serde_json::json!({"options":{"title":"hello","body":false}})).is_err());
    }
}
