use std::sync::atomic::{AtomicUsize, Ordering};
use tauri::ipc::{InvokeBody, Request, Response, Channel};
use serde_json::{json, Value};

static BLOCKED: AtomicUsize = AtomicUsize::new(0);
static PENDING_STARTED: tokio::sync::Notify = tokio::sync::Notify::const_new();
static PENDING_FINISHED: tokio::sync::Notify = tokio::sync::Notify::const_new();

#[tauri::command] fn echo(value: Value) -> Value { value }
#[tauri::command] fn blocked() -> usize { BLOCKED.fetch_add(1, Ordering::SeqCst) + 1 }
#[tauri::command] fn blocked_count() -> usize { BLOCKED.load(Ordering::SeqCst) }
#[tauri::command] fn body_info(request: Request<'_>) -> Value {
    json!({"binary": matches!(request.body(), InvokeBody::Raw(_)),
        "value": match request.body() { InvokeBody::Json(value) => value.clone(), _ => Value::Null }})
}
#[tauri::command] fn fail() -> Result<(), Value> { Err(json!({"code":"expected","detail":42})) }
#[tauri::command] fn raw(request: Request<'_>) -> Result<Response, String> {
    match request.body() { InvokeBody::Raw(bytes) => Ok(Response::new(bytes.clone())), body => Err(format!("expected raw request, received {body:?}")) }
}
#[tauri::command] fn request_info(request: Request<'_>) -> Value {
    json!({"header":request.headers().get("x-test").and_then(|v|v.to_str().ok()),
        "binary": matches!(request.body(), InvokeBody::Raw(_)),
        "value": match request.body() { InvokeBody::Json(value) => value["value"].clone(), _ => Value::Null }})
}
#[tauri::command] fn channel(channel: Channel<u32>) { for value in 0..5 { channel.send(value).unwrap(); } }
#[tauri::command] async fn pending(channel: Channel<String>) -> &'static str {
    PENDING_STARTED.notify_one();
    tokio::time::sleep(std::time::Duration::from_millis(1500)).await;
    let _ = channel.send("old".to_string());
    PENDING_FINISHED.notify_one();
    "old"
}
#[tauri::command] async fn wait_pending_started() { PENDING_STARTED.notified().await; }
#[tauri::command] async fn wait_pending() { PENDING_FINISHED.notified().await; }
#[tauri::command] fn complete(app: tauri::AppHandle, results: Value) {
    let cases = results.as_array().unwrap();
    for result in cases { println!("{} {} {}", if result["passed"] == true {"PASS"} else {"FAIL"}, result["name"], result.get("error").unwrap_or(&Value::Null)); }
    if let Some(path) = std::env::var_os("NETWIV_ISOLATION_REPORT") { std::fs::write(path, results.to_string()).unwrap(); }
    app.exit(if cases.len() == 19 && cases.iter().filter(|value|value["scope"] == "public").all(|value|value["passed"] == true) {0} else {1});
}
fn main() {
    tauri::Builder::default()
        .invoke_handler(tauri::generate_handler![echo, blocked, blocked_count, body_info, fail, raw, request_info, channel, pending, wait_pending_started, wait_pending, complete])
        .setup(|app| {
            let handle = app.handle().clone();
            std::thread::spawn(move || { std::thread::sleep(std::time::Duration::from_secs(60)); eprintln!("FAIL isolation reference timed out"); handle.exit(1); });
            Ok(())
        })
        .run(tauri::generate_context!()).expect("Tauri isolation reference failed");
}
