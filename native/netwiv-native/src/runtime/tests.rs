use std::path::{Path, PathBuf};

use crate::model::RunConfig;
use serde_json::json;
use tao::dpi::PhysicalSize;
use wry::ProxyConfig;

use super::webview::stale_eval_document;
use super::protocol::{build_asset_response, webview_origin};
use super::options::{
    constrained_inner_size,
    invalid_argument,
    parse_color,
    parse_proxy_url,
    parse_webview_options,
    parse_window_options,
    position_value,
    prevent_overflow_margin,
    resolve_webview_data_directory,
    size_value,
};

#[test]
fn window_effects_validate_ignored_platform_fields_and_unknown_keys() {
    for value in [
        json!({}), json!({"effects": null}), json!({"effects": ["unknown"]}),
        json!({"effects": [], "radius": "4"}),
        json!({"effects": [], "state": "unknown"}),
        json!({"effects": [], "color": [256, 0, 0, 255]}),
        json!({"effects": [], "unexpected": true}), json!(false),
    ] {
        let error = super::platform::decode_window_effects(Some(&value)).unwrap_err();
        assert!(error.starts_with("invalid args `value` for command `set_effects`:"), "{error}");
        // Creation uses the same pinned config decoder, before allocating a window.
        assert!(super::options::parse_window_options(&json!({"label":"effects-invalid", "windowEffects": value}), "create").is_err());
    }
}

#[test]
fn window_effects_accept_clear_and_preserve_order_and_typed_fields() {
    assert!(super::platform::decode_window_effects(None).unwrap().is_none());
    assert!(super::platform::decode_window_effects(Some(&serde_json::Value::Null)).unwrap().is_none());
    let effects = super::platform::decode_window_effects(Some(&json!({
        "effects": ["sidebar", "blur", "mica"], "radius": 4.5, "state": "active", "color": "#12345678"
    }))).unwrap().unwrap();
    assert_eq!(effects.effects, vec![tauri_utils::WindowEffect::Sidebar, tauri_utils::WindowEffect::Blur, tauri_utils::WindowEffect::Mica]);
    assert_eq!(effects.radius, Some(4.5));
    assert_eq!(effects.state, Some(tauri_utils::WindowEffectState::Active));
    assert_eq!(effects.color.map(<(u8,u8,u8,u8)>::from), Some((0x12, 0x34, 0x56, 0x78)));
}

#[cfg(not(target_os = "macos"))]
#[test]
#[ignore = "requires a native desktop; run explicitly with the desktop test wrapper"]
fn native_menu_replacement_and_accelerator_dispatch() {
    use tao::{event_loop::EventLoopBuilder, window::WindowBuilder};
    #[cfg(target_os = "windows")]
    use tao::platform::windows::EventLoopBuilderExtWindows;
    #[cfg(target_os = "linux")]
    use tao::platform::unix::EventLoopBuilderExtUnix;

    let mut builder = EventLoopBuilder::<()>::new();
    builder.with_any_thread(true);
    #[cfg(target_os = "windows")]
    let accelerators = crate::menu_accelerators::MenuAccelerators::default();
    #[cfg(target_os = "windows")]
    accelerators.install(&mut builder);
    let event_loop = builder.build();
    let window = WindowBuilder::new().with_title("NetWiv native menu regression").with_visible(false).build(&event_loop).unwrap();
    let first = muda::Menu::with_id("first");
    let second = muda::Menu::with_id("second");
    let item = muda::MenuItem::with_id("dispatch", "Dispatch", true, Some("F24".parse().unwrap()));
    second.append(&item).unwrap();
    super::menus::replace_attached_menu(None, &first, &window).unwrap();
    super::menus::replace_attached_menu(Some(&first), &second, &window).unwrap();
    // Reusing the same menu must remove the prior native attachment as well.
    super::menus::replace_attached_menu(Some(&second), &second, &window).unwrap();
    #[cfg(target_os = "linux")]
    {
        use gtk::prelude::*;
        use tao::platform::unix::WindowExtUnix;
        let bars = || window.default_vbox().unwrap().children().iter().filter(|widget| widget.is::<gtk::MenuBar>()).count();
        assert_eq!(bars(), 1, "menu replacement accumulated GTK menubars");
        super::menus::detach_menu(&second, &window).unwrap();
        assert_eq!(bars(), 0, "menu removal retained a GTK menubar");
    }
    #[cfg(target_os = "windows")]
    {
        use std::time::{Duration, Instant};
        use tao::{event::Event, event_loop::ControlFlow, platform::{run_return::EventLoopExtRunReturn, windows::WindowExtWindows}};
        use windows::Win32::{Foundation::{HWND, LPARAM, WPARAM}, UI::WindowsAndMessaging::{PostMessageW, WM_KEYDOWN}};
        let mut event_loop = event_loop;
        let post = |key| {
            // SAFETY: this posts only to the hidden test-owned window, never global keyboard input.
            unsafe { PostMessageW(Some(HWND(window.hwnd() as _)), WM_KEYDOWN, WPARAM(key), LPARAM(0)).unwrap(); }
        };
        accelerators.replace(std::iter::once(&second));
        post(0x87); // VK_F24
        let deadline = Instant::now() + Duration::from_secs(5);
        let mut events = 0;
        let mut cleared_at = None;
        event_loop.run_return(|event, _, flow| {
            *flow = ControlFlow::WaitUntil(Instant::now() + Duration::from_millis(20));
            if let Event::MainEventsCleared = event {
                while let Ok(event) = muda::MenuEvent::receiver().try_recv() {
                    assert_eq!(event.id, item.id().clone());
                    events += 1;
                }
                if events == 1 && cleared_at.is_none() {
                    item.set_accelerator(Some("F23".parse().unwrap())).unwrap();
                    post(0x87); // Old binding must stop dispatching.
                    post(0x86); // VK_F23 must use the updated native accelerator table.
                    cleared_at = Some(None);
                } else if events == 2 && cleared_at == Some(None) {
                    accelerators.replace(std::iter::empty());
                    post(0x86);
                    cleared_at = Some(Some(Instant::now()));
                }
                assert!(events <= 2, "a replaced or cleared binding still dispatched");
                if cleared_at.flatten().is_some_and(|time| time.elapsed() >= Duration::from_millis(100)) {
                    *flow = ControlFlow::Exit;
                } else {
                    assert!(Instant::now() < deadline, "menu accelerator was not dispatched");
                }
            }
        });
        assert_eq!(events, 2);
        super::menus::detach_menu(&second, &window).unwrap();
    }
}

#[test]
fn document_bound_eval_is_discarded_after_close_or_reload() {
    assert!(stale_eval_document(None, 41));
    assert!(stale_eval_document(Some(42), 41));
    assert!(!stale_eval_document(Some(41), 41));
    assert!(!stale_eval_document(None, 0));
}

#[test]
fn late_protocol_completion_after_shutdown_does_not_reenter_callbacks() {
    static CALLS: std::sync::atomic::AtomicUsize = std::sync::atomic::AtomicUsize::new(0);
    unsafe extern "C" fn callback(
        _: *mut std::ffi::c_void,
        _: u32,
        _: u64,
        _: *const u8,
        _: usize,
        _: *const u8,
        _: usize,
    ) -> i32 {
        CALLS.fetch_add(1, std::sync::atomic::Ordering::Relaxed);
        0
    }
    let shared = super::Shared::new(callback, std::ptr::null_mut());
    assert!(shared.respond_asset(42, "{}", Vec::new()).is_err());
    shared
        .state
        .store(super::STATE_STOPPED, std::sync::atomic::Ordering::Release);
    assert!(shared.respond_asset(42, "{}", Vec::new()).is_ok());
    assert!(shared.assets.lock().unwrap().is_empty());
    assert_eq!(CALLS.load(std::sync::atomic::Ordering::Relaxed), 0);
}

#[cfg(windows)]
#[test]
fn late_notification_worker_completion_after_shutdown_does_not_reenter_callbacks() {
    use std::sync::{Arc, mpsc::channel, atomic::Ordering};
    use std::time::Duration;
    unsafe extern "C" fn callback(_: *mut std::ffi::c_void, _: u32, _: u64,
        _: *const u8, _: usize, _: *const u8, _: usize) -> i32 {
        panic!("late notification callback must be discarded");
    }
    let shared = Arc::new(super::Shared::new(callback, std::ptr::null_mut()));
    shared.state.store(super::STATE_RUNNING, Ordering::Release);
    let worker_shared = shared.clone();
    let (release, waiting) = channel();
    let (complete, result) = channel();
    let worker = crate::notification::spawn_notification(move || {
        waiting.recv_timeout(Duration::from_secs(5)).unwrap();
        Ok(serde_json::Value::Null)
    }, move |outcome| {
        complete.send(worker_shared.enqueue(super::UserEvent::NotificationCompletion {
            request_id: 42, result: outcome,
        })).unwrap();
    });
    shared.state.store(super::STATE_STOPPED, Ordering::Release);
    release.send(()).unwrap();
    assert_eq!(result.recv_timeout(Duration::from_secs(5)).unwrap().unwrap_err(), "native runtime is not running");
    worker.join().unwrap();
}

#[cfg(target_os = "macos")]
#[test]
fn late_data_store_completion_after_shutdown_does_not_reenter_callbacks() {
    unsafe extern "C" fn callback(_: *mut std::ffi::c_void, _: u32, _: u64,
        _: *const u8, _: usize, _: *const u8, _: usize) -> i32 {
        panic!("late data-store callback must be discarded");
    }
    let shared = super::Shared::new(callback, std::ptr::null_mut());
    shared.state.store(super::STATE_STOPPED, std::sync::atomic::Ordering::Release);
    for result in [Ok(json!([])), Err("store is in use".into())] {
        assert!(shared.enqueue(super::UserEvent::DataStoreCompletion { request_id: 42, result }).is_err());
    }
}

#[test]
fn reparent_updates_ipc_and_protocol_parent_without_replacing_document() {
    unsafe extern "C" fn callback(
        _: *mut std::ffi::c_void,
        _: u32,
        _: u64,
        _: *const u8,
        _: usize,
        _: *const u8,
        _: usize,
    ) -> i32 {
        0
    }
    let shared = super::Shared::new(callback, std::ptr::null_mut());
    let epoch = std::sync::Arc::new(std::sync::atomic::AtomicU64::new(42));
    shared
        .register_webview_origin(
            "child",
            "tauri://localhost".into(),
            "allowed".into(),
            epoch.clone(),
            #[cfg(target_os = "linux")]
            Default::default(),
        )
        .unwrap();
    assert_eq!(shared.webview_parent("child").as_deref(), Some("allowed"));
    shared.reparent_document("child", "denied").unwrap();
    assert_eq!(shared.webview_parent("child").as_deref(), Some("denied"));
    {
        let documents = shared.webview_origins.lock().unwrap();
        let document = documents.get("child").unwrap();
        assert_eq!(document.window_label, "denied");
        assert_eq!(document.origin, "tauri://localhost");
        assert!(std::sync::Arc::ptr_eq(&document.document_id, &epoch));
    }
    shared.remove_webview_origin("child");
    assert!(shared.webview_parent("child").is_none());
    assert!(shared.reparent_document("child", "allowed").is_err());
}

#[test]
fn asset_protocol_config_is_additive_and_empty_content_type_is_omitted() {
    let disabled: RunConfig = serde_json::from_value(json!({
        "initializationScript":"",
        "windows":[]
    }))
    .unwrap();
    assert!(!disabled.asset_protocol_enabled);
    assert!(!disabled.ipc_protocol_enabled);
    assert!(disabled.isolation_protocol.is_none());
    assert!(disabled.uri_scheme_protocols.is_empty());
    assert!(!disabled.track_webview_parents);
    for protocol in [
        "asset",
        "http",
        "isolation-",
        "isolation-UPPER",
        "isolation-bad:",
    ] {
        let invalid: RunConfig = serde_json::from_value(json!({
            "initializationScript":"", "windows":[], "isolationProtocol":protocol
        }))
        .unwrap();
        assert!(
            crate::model::validate_config(&invalid)
                .unwrap_err()
                .contains("isolationProtocol")
        );
    }
    let enabled: RunConfig = serde_json::from_value(json!({
        "initializationScript":"",
        "windows":[],
        "assetProtocolEnabled":true,
        "ipcProtocolEnabled":true,
        "trackWebviewParents":true,
        "uriSchemeProtocols":["contract-sync", "contract-async"]
    }))
    .unwrap();
    assert!(enabled.asset_protocol_enabled);
    assert!(enabled.ipc_protocol_enabled);
    assert!(enabled.track_webview_parents);
    assert_eq!(
        enabled.uri_scheme_protocols,
        ["contract-sync", "contract-async"]
    );

    let response = build_asset_response(
        r#"{"statusCode":404,"contentType":"","headers":{"access-control-allow-origin":"tauri://localhost"}}"#,
        Vec::new(),
    )
    .unwrap();
    assert_eq!(response.status(), 404);
    assert!(response.headers().get("content-type").is_none());
    assert_eq!(
        response
            .headers()
            .get("access-control-allow-origin")
            .unwrap(),
        "tauri://localhost"
    );
}

#[test]
fn initial_webview_origin_matches_pinned_tauri_rules() {
    let origin = webview_origin(&crate::model::resolve_url("index.html", true), true);
    assert_eq!(
        origin,
        if cfg!(target_os = "windows") {
            "https://tauri.localhost"
        } else {
            "tauri://localhost"
        }
    );
    assert!(
        super::protocol::bridge_script("main", "secondary", true, true, false)
            .contains("protocolScheme:\"https\"")
    );
    assert!(!super::protocol::bridge_script("main", "secondary", false, true, false)
        .contains("postFileMessage"));
    if cfg!(target_os = "windows") {
        let script = super::protocol::bridge_script("main", "secondary", false, true, true);
        assert!(script.contains("postMessageWithAdditionalObjects"));
        assert!(script.contains("JSON.stringify({__NETWIV_FILE_PATH__:serialized})"));
        assert!(script.contains("FilePathNotSupported"));
    }
    assert_eq!(webview_origin("data:text/html,hello", false), "null");
    assert_eq!(
        webview_origin("https://example.test:8443/page", false),
        "https://example.test:8443"
    );
    if cfg!(target_os = "windows") {
        assert_eq!(
            webview_origin("tauri://localhost/page", false),
            "http://tauri.localhost"
        );
        assert_eq!(
            webview_origin("asset://localhost/file", true),
            "https://asset.localhost"
        );
    } else {
        assert_eq!(
            webview_origin("tauri://localhost/page", false),
            "tauri://localhost"
        );
        assert_eq!(
            webview_origin("asset://localhost/file", true),
            "asset://localhost"
        );
    }
}

#[test]
fn prevent_overflow_reduces_inner_size_by_outer_excess() {
    let size = constrained_inner_size(
        PhysicalSize::new(1920, 1080),
        PhysicalSize::new(1936, 1119),
        PhysicalSize::new(1600, 900),
        PhysicalSize::new(20, 30),
    );
    assert_eq!(size, PhysicalSize::new(1564, 831));
    assert_eq!(
        prevent_overflow_margin(Some(&json!(true))).unwrap(),
        Some(PhysicalSize::new(0, 0))
    );
    assert_eq!(
        prevent_overflow_margin(Some(&json!({"width":12,"height":34}))).unwrap(),
        Some(PhysicalSize::new(12, 34))
    );
}

#[test]
fn data_directory_matches_pinned_from_config_resolution() {
    let root = Path::new("local-data");
    let default = Path::new("app-data");
    assert_eq!(
        resolve_webview_data_directory(Some(root), Some(default), "main", Some(&json!("profile"))).unwrap(),
        Some(root.join("main").join("profile"))
    );
    assert_eq!(
        resolve_webview_data_directory(Some(root), Some(default), "main", Some(&json!("../shared"))).unwrap(),
        Some(PathBuf::from("../shared"))
    );
    let absolute = std::env::temp_dir().join("netwiv-explicit-profile");
    assert_eq!(
        resolve_webview_data_directory(Some(root), Some(default), "main", Some(&json!(absolute))).unwrap(),
        Some(absolute)
    );
    assert!(resolve_webview_data_directory(Some(root), Some(default), "main", Some(&json!(12))).is_err());
}

#[test]
fn default_data_directory_is_windows_only_and_app_scoped() {
    let first = Path::new("app-data/first");
    let second = Path::new("app-data/second");
    let expected = if cfg!(target_os = "windows") { Some(first.to_path_buf()) } else { None };
    for value in [None, Some(json!(null))] {
        assert_eq!(resolve_webview_data_directory(None, Some(first), "main", value.as_ref()).unwrap(), expected);
        assert_eq!(resolve_webview_data_directory(None, Some(first), "child", value.as_ref()).unwrap(), expected);
    }
    let other = resolve_webview_data_directory(None, Some(second), "main", None).unwrap();
    if cfg!(target_os = "windows") { assert_ne!(other, expected); }
    else { assert_eq!(other, None); }
    assert_eq!(resolve_webview_data_directory(None, None, "main", None).unwrap(), None);
}

#[test]
fn proxy_parser_preserves_explicit_host_and_port() {
    match parse_proxy_url(Some(&json!("http://proxy.example:3128"))).unwrap() {
        Some(ProxyConfig::Http(endpoint)) => {
            assert_eq!(endpoint.host, "proxy.example");
            assert_eq!(endpoint.port, "3128");
        }
        _ => panic!("expected HTTP proxy"),
    }
    assert!(parse_proxy_url(Some(&json!("https://proxy.example"))).is_err());
}

#[test]
fn parse_hex_color() {
    for (input, expected) in [
        ("fff", (255, 255, 255, 255)),
        ("#fff", (255, 255, 255, 255)),
        ("#000000", (0, 0, 0, 255)),
        ("#000000ff", (0, 0, 0, 255)),
        ("#00ff00ff", (0, 255, 0, 255)),
    ] {
        assert_eq!(parse_color(&json!(input)).unwrap(), expected);
    }
}

#[test]
fn public_color_shapes_match_tauri_config_deserialization() {
    assert_eq!(parse_color(&json!("#abc")).unwrap(), (170, 187, 204, 255));
    assert_eq!(parse_color(&json!("11223344")).unwrap(), (17, 34, 51, 68));
    assert_eq!(parse_color(&json!([1, 2, 3])).unwrap(), (1, 2, 3, 255));
    assert_eq!(parse_color(&json!([1, 2, 3, 4])).unwrap(), (1, 2, 3, 4));
    assert_eq!(
        parse_color(&json!({"red":1,"green":2,"blue":3})).unwrap(),
        (1, 2, 3, 255)
    );
    assert!(parse_color(&json!([1, 2])).is_err());
    assert!(parse_color(&json!("#12")).is_err());
    assert!(parse_color(&json!({"red":1,"green":2,"blue":300})).is_err());
    assert_eq!(
        invalid_argument(
            "set_background_color",
            "value",
            parse_color(&json!("#ab")).unwrap_err()
        ),
        "invalid args `value` for command `set_background_color`: Invalid hex color length, must be either 3, 6 or 8, for example: #fff, #ffffff, or #ffffffff"
    );
    assert_eq!(
        invalid_argument(
            "set_badge_count",
            "value",
            serde_json::from_value::<Option<i64>>(json!(1e30)).unwrap_err()
        ),
        "invalid args `value` for command `set_badge_count`: invalid type: floating point `1e+30`, expected i64"
    );
}

#[test]
fn dynamic_labels_and_numeric_options_are_validated_before_native_creation() {
    assert!(parse_window_options(&json!({"label":""}), "create").is_ok());
    assert!(parse_window_options(&json!({"label":"alpha-1/:_"}), "create").is_ok());
    assert!(parse_window_options(&json!({"label":"bad.label"}), "create").is_err());
    assert_eq!(
        parse_window_options(
            &json!({"label":"main","width":"bad"}),
            "create_webview_window"
        )
        .unwrap_err(),
        "invalid args `options` for command `create_webview_window`: invalid type: string \"bad\", expected f64"
    );
    assert_eq!(
        parse_window_options(
            &json!({"label":"main","visible":"bad"}),
            "create_webview_window"
        )
        .unwrap_err(),
        "invalid args `options` for command `create_webview_window`: invalid type: string \"bad\", expected a boolean"
    );
    assert!(
        parse_window_options(&json!({"label":"main","notAnOption":true}), "create").is_err()
    );
    assert!(
        parse_webview_options(
            &json!({"label":"view","url":"index.html","x":"left"}),
            "create_webview"
        )
        .is_err()
    );
}

#[test]
fn window_config_defaults_use_netwiv_title() {
    let mut default = tauri_utils::config::WindowConfig::default();
    default.title = "NetWiv App".into();
    let deserialized = parse_window_options(&json!({}), "create").unwrap();
    let explicit = parse_window_options(&serde_json::to_value(&default).unwrap(), "create").unwrap();
    assert_eq!(deserialized.label, default.label);
    assert_eq!(deserialized.title, default.title);
    assert_eq!(deserialized.url, default.url.to_string());
    assert_eq!(deserialized.width, default.width);
    assert_eq!(deserialized.height, default.height);
    assert_eq!(deserialized.extra_config, explicit.extra_config);
    assert!(deserialized.extra_config["focus"].as_bool().unwrap());
    assert!(deserialized.extra_config.get("windowEffects").is_none());
}

#[test]
fn window_titles_preserve_explicit_values_and_reject_invalid_types() {
    for title in ["Custom", "", "Tauri App"] {
        let config = parse_window_options(&json!({"title":title}), "create").unwrap();
        assert_eq!(config.title, title);
        assert_eq!(config.extra_config["title"], title);
    }
    for title in [json!(null), json!(42), json!(false)] {
        assert!(parse_window_options(&json!({"title":title}), "create").is_err());
    }
}

#[test]
fn window_and_webview_creation_preserve_aliases_after_validation() {
    let aliases = json!({"label":"alias", "user-agent":"custom-agent", "drag-drop-enabled":false,
        "always-on-top":true, "min-width":120, "data-directory":"profile"});
    let camel = json!({"label":"alias", "userAgent":"custom-agent", "dragDropEnabled":false,
        "alwaysOnTop":true, "minWidth":120, "dataDirectory":"profile"});
    let window = parse_window_options(&aliases, "create").unwrap();
    assert_eq!(window.extra_config, parse_window_options(&camel, "create").unwrap().extra_config);
    let view = parse_webview_options(&aliases, "create_webview").unwrap();
    assert_eq!(view.extra_config, parse_webview_options(&camel, "create_webview").unwrap().extra_config);
    assert_eq!(view.extra_config["userAgent"], "custom-agent");
    assert_eq!(view.extra_config["dragDropEnabled"], false);
    assert_eq!(window.extra_config["alwaysOnTop"], true);
    assert_eq!(window.extra_config["minWidth"], 120.0);
    assert!(parse_window_options(&json!({"user-agent":"a","userAgent":"b"}), "create").is_err());
}

#[test]
fn backend_child_builder_options_do_not_leak_into_the_guest_contract() {
    let options = json!({"label":"child", "autoResize":true, "visible":false});
    let backend = super::options::parse_child_webview_options(&options, true).unwrap();
    assert_eq!(backend.extra_config["autoResize"], true);
    assert_eq!(backend.extra_config["visible"], false);
    assert!(super::options::parse_child_webview_options(&options, false).unwrap_err().contains("unknown field `autoResize`"));
    assert!(super::options::parse_child_webview_options(&json!({"autoResize":"bad"}), true).is_err());
    let guest = super::options::parse_child_webview_options(&json!({"visible":false}), false).unwrap();
    assert!(guest.extra_config.get("visible").is_none());
    assert!(super::options::parse_child_webview_options(&json!({"unknown":true}), true).is_err());
}

#[test]
fn physical_size_and_position_reject_fractional_and_out_of_range_values() {
    assert_eq!(
        invalid_argument(
            "set_size",
            "value",
            size_value(&json!({"Physical":{"width":1.5,"height":1}})).unwrap_err()
        ),
        "invalid args `value` for command `set_size`: invalid type: floating point `1.5`, expected u32"
    );
    assert_eq!(
        invalid_argument(
            "set_size",
            "value",
            size_value(&json!({"Physical":{"width":4294967296_u64,"height":1}})).unwrap_err()
        ),
        "invalid args `value` for command `set_size`: invalid value: integer `4294967296`, expected u32"
    );
    assert_eq!(
        invalid_argument(
            "set_position",
            "value",
            position_value(&json!({"Physical":{"x":0.5,"y":0}})).unwrap_err()
        ),
        "invalid args `value` for command `set_position`: invalid type: floating point `0.5`, expected i32"
    );
    assert_eq!(
        invalid_argument(
            "set_position",
            "value",
            position_value(&json!({"Physical":{"x":2147483648_i64,"y":0}})).unwrap_err()
        ),
        "invalid args `value` for command `set_position`: invalid value: integer `2147483648`, expected i32"
    );
    assert_eq!(
        size_value(&json!({"Physical":{"width":2,"height":3}})).unwrap(),
        (2.0, 3.0, false)
    );
    assert_eq!(
        position_value(&json!({"Physical":{"x":-2,"y":3}})).unwrap(),
        (-2.0, 3.0, false)
    );
}
