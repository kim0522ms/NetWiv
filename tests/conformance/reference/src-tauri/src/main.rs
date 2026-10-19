use std::{io::Write, path::PathBuf};
use tauri::{ipc::{Channel, Response}, Manager, State};
use raw_window_handle::{HasWindowHandle, RawWindowHandle};

mod lifetime_fixture;

fn parse_expected_backend(value: Option<&str>) -> Result<Option<&str>, &'static str> {
    match value {
        None => Ok(None),
        Some("x11" | "wayland" | "windows" | "macos") => Ok(value),
        _ => Err("NETWIV_EXPECT_BACKEND must be x11, wayland, windows or macos."),
    }
}

fn verify_backend(expected: Option<&str>, actual: &str) -> Result<(), String> {
    if let Some(expected) = expected {
        if expected != actual {
            return Err(format!("Expected native backend {expected}, observed {actual}."));
        }
    }
    Ok(())
}

fn backend_from_handle(handle: RawWindowHandle) -> Result<&'static str, &'static str> {
    match handle {
        RawWindowHandle::Wayland(_) => Ok("wayland"),
        RawWindowHandle::Xlib(_) | RawWindowHandle::Xcb(_) => Ok("x11"),
        RawWindowHandle::Win32(_) => Ok("windows"),
        RawWindowHandle::AppKit(_) => Ok("macos"),
        _ => Err("Unknown native window backend"),
    }
}

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
fn conformance_record(result: serde_json::Value) -> Result<(), String> {
    let mut output = std::io::stdout().lock();
    writeln!(output, "NETWIV_CONFORMANCE {result}").map_err(|_| "Cannot emit conformance result".to_string())
}

#[tauri::command]
fn conformance_finish(app: tauri::AppHandle) { app.exit(0); }

#[tauri::command]
fn conformance_profile() -> &'static str { if cfg!(debug_assertions) { "debug" } else { "release" } }

#[tauri::command]
async fn create_data_store_fixture(app: tauri::AppHandle, uuid: [u8; 16], url: String, incognito: bool) -> Result<(), String> {
    tauri::WebviewWindowBuilder::new(&app, "conformance-child", tauri::WebviewUrl::App(url.into()))
        .data_store_identifier(uuid).incognito(incognito).visible(false).build().map_err(|error| error.to_string())?;
    Ok(())
}

#[tauri::command]
fn conformance_platform() -> Result<serde_json::Value, String> {
    #[cfg(target_os = "macos")]
    let supported = {
        let output = std::process::Command::new("/usr/bin/sw_vers").arg("-productVersion")
            .output().map_err(|_| "Cannot determine website data-store availability")?;
        if !output.status.success() { return Err("Cannot determine website data-store availability".into()); }
        let major = std::str::from_utf8(&output.stdout).ok().and_then(|version| version.trim().split('.').next())
            .and_then(|major| major.parse::<u32>().ok()).ok_or("Cannot determine website data-store availability")?;
        major >= 14
    };
    #[cfg(not(target_os = "macos"))]
    let supported = false;
    let os = if cfg!(target_os = "macos") { "macos" } else if cfg!(target_os = "windows") { "windows" } else { "linux" };
    Ok(serde_json::json!({"os": os, "websiteDataStores": supported}))
}

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
    let expected_backend = std::env::var_os("NETWIV_EXPECT_BACKEND")
        .map(|value| value.into_string().unwrap_or_else(|_| panic!("Invalid native backend expectation")));
    let expected_backend = parse_expected_backend(expected_backend.as_deref())
        .expect("Invalid native backend expectation").map(str::to_owned);
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
        .invoke_handler(tauri::generate_handler![asset_fixture, echo_json, always_fails, echo_bytes, stream_numbers, custom_default_allowed, typed_request, restricted, scope_read, scope_write, conformance_record, conformance_finish, conformance_profile, conformance_cases, conformance_platform, create_data_store_fixture])
        .setup(move |app| {
            let window = app.get_webview_window("main").ok_or("Missing main window for backend assertion")?;
            let actual = backend_from_handle(window.window_handle()?.as_raw())?;
            verify_backend(expected_backend.as_deref(), actual)?;
            println!("BACKEND {actual}");
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

#[cfg(test)]
mod tests {
    use super::{backend_from_handle, parse_expected_backend, verify_backend};
    use raw_window_handle::{RawWindowHandle, WaylandWindowHandle, WebWindowHandle, XlibWindowHandle};

    #[test]
    fn backend_uses_native_handle_kind_and_rejects_unknown_platforms() {
        // The classifier reads the handle kind only; it never dereferences these placeholders.
        let wayland = WaylandWindowHandle::new(std::ptr::NonNull::dangling());
        assert_eq!(backend_from_handle(RawWindowHandle::Wayland(wayland)), Ok("wayland"));
        assert_eq!(backend_from_handle(RawWindowHandle::Xlib(XlibWindowHandle::new(1))), Ok("x11"));
        assert!(backend_from_handle(RawWindowHandle::Web(WebWindowHandle::new(1))).is_err());
    }

    #[test]
    fn backend_expectation_accepts_only_exact_known_names() {
        assert_eq!(parse_expected_backend(None), Ok(None));
        for name in ["x11", "wayland", "windows", "macos"] {
            assert_eq!(parse_expected_backend(Some(name)), Ok(Some(name)));
        }
        for invalid in ["", "Wayland", "wayland,x11", "unknown"] {
            assert!(parse_expected_backend(Some(invalid)).is_err());
        }
    }

    #[test]
    fn backend_mismatch_is_a_failure() {
        assert!(verify_backend(None, "x11").is_ok());
        assert!(verify_backend(Some("wayland"), "wayland").is_ok());
        assert_eq!(verify_backend(Some("wayland"), "x11"),
            Err("Expected native backend wayland, observed x11.".into()));
    }
}
