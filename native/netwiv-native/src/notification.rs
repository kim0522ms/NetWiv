use serde_json::Value;

pub(crate) fn notify(
    args: &Value,
    #[cfg(windows)] complete: impl FnOnce(Result<Value, String>) + Send + 'static,
) -> Result<Value, String> {
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
    let show = move || {
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
        notification.show().map(|_| Value::Null).map_err(|error| error.to_string())
    };
    #[cfg(windows)]
    let _ = spawn_notification(show, complete);
    #[cfg(not(windows))]
    std::thread::spawn(move || { let _ = show(); });
    Ok(Value::Null)
}

#[cfg(windows)]
pub(crate) fn spawn_notification(
    show: impl FnOnce() -> Result<Value, String> + Send + 'static,
    complete: impl FnOnce(Result<Value, String>) + Send + 'static,
) -> std::thread::JoinHandle<()> {
    std::thread::spawn(move || complete(show()))
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
        for args in [
            serde_json::json!({}),
            serde_json::json!({"options":{"title":2}}),
            serde_json::json!({"options":{"title":"hello","body":false}}),
        ] {
            #[cfg(windows)]
            let result = super::notify(&args, |_| panic!("invalid payload dispatched a worker"));
            #[cfg(not(windows))]
            let result = super::notify(&args);
            assert!(result.is_err());
        }
    }

    #[cfg(windows)]
    #[test]
    fn worker_completes_once_with_success_or_error_without_blocking_the_caller() {
        use std::sync::mpsc::{channel, TryRecvError};
        use std::time::Duration;
        let caller = std::thread::current().id();
        for expected in [Ok(serde_json::Value::Null), Err("synthetic show failure".to_owned())] {
            let outcome = expected.clone();
            let (release, waiting) = channel();
            let (complete, result) = channel();
            let worker = super::spawn_notification(move || {
                waiting.recv_timeout(Duration::from_secs(5)).unwrap();
                outcome
            }, move |outcome| {
                complete.send((std::thread::current().id(), outcome)).unwrap();
            });
            assert!(matches!(result.try_recv(), Err(TryRecvError::Empty)));
            release.send(()).unwrap();
            let (thread, actual) = result.recv_timeout(Duration::from_secs(5)).unwrap();
            assert_ne!(thread, caller);
            assert_eq!(actual, expected);
            worker.join().unwrap();
            assert!(matches!(result.try_recv(), Err(TryRecvError::Disconnected)));
        }
    }
}
