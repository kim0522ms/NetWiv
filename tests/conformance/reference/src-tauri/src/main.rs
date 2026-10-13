use std::{fs::OpenOptions, io::Write, path::PathBuf, sync::Mutex};
use tauri::{ipc::{Channel, Response}, Manager, State};

mod lifetime_fixture;

struct Report(Mutex<PathBuf>);
struct AssetFixture(PathBuf);
impl Drop for AssetFixture {
    fn drop(&mut self) { let _ = std::fs::remove_dir_all(&self.0); }
}

#[tauri::command]
fn asset_fixture(fixture: State<'_, AssetFixture>) -> String { fixture.0.to_string_lossy().into_owned() }

#[derive(Clone, serde::Deserialize, serde::Serialize)]
#[serde(rename_all = "camelCase")]
struct TypedRequest { message: String, count: u32 }

struct PluginSettings(TypedRequest);

#[tauri::command]
fn read_config(config: State<'_, PluginSettings>) -> TypedRequest { config.0.clone() }

#[tauri::command]
fn echo_json(input: serde_json::Value) -> serde_json::Value { input }

#[tauri::command]
fn always_fails() -> Result<(), &'static str> { Err("intentional conformance error") }

#[tauri::command]
fn echo_bytes(bytes: Vec<u8>) -> Response { Response::new(bytes) }

#[tauri::command]
async fn stream_numbers(app: tauri::AppHandle, channel: Channel<u32>, resume_event: Option<String>) -> Result<(), String> {
    lifetime_fixture::stream_numbers(app, channel, resume_event).await
}

#[tauri::command]
fn custom_default_allowed() -> &'static str { "allowed" }

#[tauri::command]
fn typed_request(request: TypedRequest) -> TypedRequest { request }

#[tauri::command]
fn restricted() -> &'static str { "must be denied without its permission" }

fn scope_value(scope: tauri::ipc::CommandScope<serde_json::Value>) -> serde_json::Value {
    serde_json::json!({
        "allow": scope.allows().iter().map(|value| value.as_ref()).collect::<Vec<_>>(),
        "deny": scope.denies().iter().map(|value| value.as_ref()).collect::<Vec<_>>()
    })
}

#[tauri::command]
fn scope_read(scope: tauri::ipc::CommandScope<serde_json::Value>) -> serde_json::Value { scope_value(scope) }

#[tauri::command]
fn scope_write(scope: tauri::ipc::CommandScope<serde_json::Value>) -> serde_json::Value { scope_value(scope) }

#[tauri::command]
fn conformance_record(result: serde_json::Value, report: State<'_, Report>) -> Result<(), String> {
    let path = report.0.lock().map_err(|error| error.to_string())?;
    let mut file = OpenOptions::new().create(true).append(true).open(&*path).map_err(|error| error.to_string())?;
    writeln!(file, "{result}").map_err(|error| error.to_string())
}

#[tauri::command]
fn conformance_finish(app: tauri::AppHandle) { app.exit(0); }

#[tauri::command]
fn conformance_profile() -> &'static str { if cfg!(debug_assertions) { "debug" } else { "release" } }

#[tauri::command]
fn conformance_cases() -> Result<Option<Vec<String>>, String> {
    serde_json::from_str(&std::env::var("NETWIV_CONFORMANCE_CASES").unwrap_or_else(|_| "null".into()))
        .map_err(|error| error.to_string())
}

fn protocol_response(label: &str, request: tauri::http::Request<Vec<u8>>, status: u16) -> tauri::http::Response<Vec<u8>> {
    let response = tauri::http::Response::builder()
        .header("Access-Control-Allow-Origin", "*").header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
        .header("Access-Control-Allow-Headers", "x-request,content-type")
        .header("Access-Control-Expose-Headers", "X-Webview,X-Method,X-Request")
        .header("X-Webview", label).header("X-Method", request.method().as_str())
        .header("X-Request", request.headers().get("X-Request").and_then(|v| v.to_str().ok()).unwrap_or(""));
    if request.method() == "OPTIONS" { return response.status(204).body(Vec::new()).unwrap(); }
    let asset: Option<(&str, &[u8])> = match request.uri().path() {
        "/page" => Some(("text/html", include_bytes!("../../../frontend/protocol.html"))),
        "/core.js" => Some(("text/javascript", include_bytes!("../../../dist/vendor/@tauri-apps/api/core.js"))),
        "/event.js" => Some(("text/javascript", include_bytes!("../../../dist/vendor/@tauri-apps/api/event.js"))),
        "/external/tslib/tslib.es6.js" => Some(("text/javascript", include_bytes!("../../../dist/vendor/@tauri-apps/api/external/tslib/tslib.es6.js"))),
        _ => None,
    };
    match asset {
        Some((mime, bytes)) => response.status(200).header("Content-Type", mime).body(bytes.to_vec()).unwrap(),
        None => response.status(status).header("Content-Type", "application/octet-stream").body(request.into_body()).unwrap(),
    }
}

fn main() {
    let assets = std::env::temp_dir().join("netwiv-conformance-assets").join(format!("reference-{}", std::process::id()));
    std::fs::create_dir_all(&assets).unwrap();
    std::fs::create_dir_all(assets.join("folder")).unwrap();
    std::fs::write(assets.join("sample # 한글.txt"), b"0123456789").unwrap();
    std::fs::write(assets.join("secret.txt"), b"secret").unwrap();
    for (name, bytes) in [
        ("rgba.png", include_bytes!("../../../frontend/images/rgba.png").as_slice()),
        ("gray16.png", include_bytes!("../../../frontend/images/gray16.png").as_slice()),
        ("palette.png", include_bytes!("../../../frontend/images/palette.png").as_slice()),
        ("multi.ico", include_bytes!("../../../frontend/images/multi.ico").as_slice()),
    ] { std::fs::write(assets.join(name), bytes).unwrap(); }
    std::fs::write(assets.join("bytes.bin"), (0..=255_u8).collect::<Vec<_>>()).unwrap();
    let fixture = AssetFixture(assets);
    let output = std::env::var_os("NETWIV_CONFORMANCE_RESULT")
        .map(PathBuf::from)
        .unwrap_or_else(|| PathBuf::from("results/reference.jsonl"));
    if let Some(parent) = output.parent() { std::fs::create_dir_all(parent).unwrap(); }
    let _ = std::fs::remove_file(&output);
    tauri::Builder::default()
        .register_uri_scheme_protocol("contract-sync", |context, request| protocol_response(context.webview_label(), request, 201))
        .register_asynchronous_uri_scheme_protocol("contract-async", |context, request, responder| {
            let label = context.webview_label().to_string();
            std::thread::spawn(move || {
                std::thread::sleep(std::time::Duration::from_millis(10));
                responder.respond(protocol_response(&label, request, 202));
            });
        })
        .plugin(tauri::plugin::Builder::<_, TypedRequest>::new("contract")
            .setup(|app, api| { app.manage(PluginSettings(api.config().clone())); Ok(()) })
            .invoke_handler(tauri::generate_handler![read_config]).build())
        .manage(fixture)
        .manage(Report(Mutex::new(output)))
        .invoke_handler(tauri::generate_handler![asset_fixture, echo_json, always_fails, echo_bytes, stream_numbers, custom_default_allowed, typed_request, restricted, scope_read, scope_write, conformance_record, conformance_finish, conformance_profile, conformance_cases])
        .on_page_load(|webview, payload| {
            eprintln!("reference page load {:?}: {:?}", payload.event(), webview.url());
        })
        .setup(|app| {
            let handle = app.handle().clone();
            std::thread::spawn(move || {
                std::thread::sleep(std::time::Duration::from_secs(60));
                handle.exit(124);
            });
            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("Tauri reference runtime failed");
}
