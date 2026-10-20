use std::sync::Arc;
use std::sync::atomic::{AtomicU64, Ordering};
use serde_json::{Value, json};
use tao::dpi::{LogicalPosition, LogicalSize};
use wry::{DragDropEvent, PageLoadEvent, Rect, WebContext, WebViewBuilder};
use crate::model::{Submission, WebviewConfig, bool_arg, f64_arg, required_string, resolve_url};
use crate::relative_bounds::RelativeBounds;
use super::{
    LocalRuntime,
    WindowState,
    WebviewState,
    WebContextState,
    CALLBACK_IPC,
    CALLBACK_EVENT,
    CALLBACK_DOCUMENT,
};
use super::protocol::{bridge_script, webview_origin};
use super::options::{
    parse_child_webview_options,
    invalid_argument,
    size_value,
    position_value,
    size_json,
    position_json,
    optional_bool,
    resolve_webview_data_directory,
    parse_proxy_url,
    optional_color_arg,
};

impl LocalRuntime {
    pub(super) fn create_webview_in(
        &mut self,
        window: &mut WindowState,
        config: WebviewConfig,
        dynamic: bool,
        implicit_root: bool,
    ) -> Result<(), String> {
        if self.webviews.contains_key(&config.label) {
            return Err(format!("webview '{}' already exists", config.label));
        }
        let drag_drop_enabled =
            optional_bool(&config.extra_config, "dragDropEnabled")?.unwrap_or(true);
        let transparent = optional_bool(&config.extra_config, "transparent")?;
        // Window visibility must not hide its root controller independently:
        // showing the parent later must also expose the root WebView.
        let visible = if implicit_root {
            None
        } else {
            optional_bool(&config.extra_config, "visible")?
        };
        let incognito = optional_bool(&config.extra_config, "incognito")?;
        let focus = optional_bool(&config.extra_config, "focus")?;
        let accept_first_mouse = optional_bool(&config.extra_config, "acceptFirstMouse")?;
        let general_autofill_enabled =
            optional_bool(&config.extra_config, "generalAutofillEnabled")?;
        let javascript_disabled =
            optional_bool(&config.extra_config, "javascriptDisabled")?.unwrap_or(false);
        let zoom_hotkeys_enabled = optional_bool(&config.extra_config, "zoomHotkeysEnabled")?;
        let devtools = optional_bool(&config.extra_config, "devtools")?;
        let auto_resize = optional_bool(&config.extra_config, "autoResize")?.unwrap_or(false);
        let use_https_scheme =
            optional_bool(&config.extra_config, "useHttpsScheme")?.unwrap_or(false);
        let resolved_url = resolve_url(&config.url, use_https_scheme);
        let window_origin = webview_origin(&resolved_url, use_https_scheme);
        let doc = Arc::new(AtomicU64::new(0));
        #[cfg(target_os = "linux")]
        let ipc = Arc::new(std::sync::Mutex::new(super::ipc::DocumentIpc::default()));
        let bounds = if implicit_root {
            Rect {
                position: LogicalPosition::new(0, 0).into(),
                size: window.window.inner_size().into(),
            }
        } else {
            Rect {
                position: LogicalPosition::new(config.x, config.y).into(),
                size: LogicalSize::new(config.width, config.height).into(),
            }
        };
        let auto_resize = if implicit_root {
            Some(RelativeBounds::FULL)
        } else if auto_resize {
            Some(RelativeBounds::new(
                bounds,
                window.window.inner_size(),
                window.window.scale_factor(),
            ))
        } else {
            None
        };
        let bridge = bridge_script(
            &window.label,
            &config.label,
            use_https_scheme,
            self.ipc_protocol_enabled,
        );
        let shared_ipc = self.shared.clone();
        let ipc_label = config.label.clone();
        let ipc_doc = doc.clone();
        #[cfg(target_os = "linux")]
        let ipc_message = ipc.clone();
        let shared_page = self.shared.clone();
        let page_label = config.label.clone();
        let page_doc = doc.clone();
        #[cfg(target_os = "linux")]
        let page_ipc = ipc.clone();
        let shared_tauri_asset = self.shared.clone();
        let shared_file_asset = self.shared.clone();
        let shared_isolation_asset = self.shared.clone();
        let shared_ipc_protocol = self.shared.clone();
        let data_directory = resolve_webview_data_directory(
            self.local_data_directory.as_deref(),
            &config.label,
            config.extra_config.get("dataDirectory"),
        )?;
        let context_key = data_directory.clone();
        let context = self
            .web_contexts
            .entry(context_key.clone())
            .or_insert_with(|| WebContextState {
                native: WebContext::new(data_directory),
                webviews: Default::default(),
                tauri_protocol_registered: false,
                asset_protocol_registered: false,
                isolation_protocol_registered: false,
                ipc_protocol_registered: false,
                uri_scheme_protocols_registered: false,
            });
        let register_tauri_protocol = !self
            .uri_scheme_protocols
            .iter()
            .any(|scheme| scheme == "tauri")
            && (!cfg!(target_os = "linux") || !context.tauri_protocol_registered);
        let register_asset_protocol = self.asset_protocol_enabled
            && !self
                .uri_scheme_protocols
                .iter()
                .any(|scheme| scheme == "asset")
            && (!cfg!(target_os = "linux") || !context.asset_protocol_registered);
        let register_isolation_protocol = self.isolation_protocol.is_some()
            && (!cfg!(target_os = "linux") || !context.isolation_protocol_registered);
        let register_ipc_protocol = self.ipc_protocol_enabled
            && !self
                .uri_scheme_protocols
                .iter()
                .any(|scheme| scheme == "ipc")
            && (!cfg!(target_os = "linux") || !context.ipc_protocol_registered);
        let register_uri_scheme_protocols =
            !cfg!(target_os = "linux") || !context.uri_scheme_protocols_registered;
        let mut builder = WebViewBuilder::new_with_web_context(&mut context.native)
            .with_id(&config.label)
            .with_bounds(bounds)
            .with_url(resolved_url)
            // Match Tauri's platform-specific bridge availability: WebView2
            // includes child frames, whereas WebKitGTK uses the main frame.
            // WebKitGTK has no native sender-frame metadata. Its main document
            // must prove the native-issued capability before source attribution.
            .with_initialization_script_for_main_only(bridge, !cfg!(target_os = "windows"))
            .with_initialization_script_for_main_only(self.initialization_script.clone(), !cfg!(target_os = "windows"))
            .with_ipc_handler(move |request| {
                #[cfg(not(target_os = "linux"))]
                let source = request.uri().to_string();
                #[cfg(not(target_os = "linux"))]
                let document_id = ipc_doc.load(Ordering::Acquire);
                #[cfg(not(target_os = "linux"))]
                let payload = request.body().clone();
                #[cfg(target_os = "linux")]
                let (document_id, payload, source) = {
                    let Ok(ipc) = ipc_message.lock() else { return; };
                    let Some(message) = ipc.message(request.body()) else { return; };
                    message
                };
                #[cfg(target_os = "linux")]
                if ipc_doc.load(Ordering::Acquire) != document_id { return; }
                if let Some(ipc_window) = shared_ipc.webview_parent(&ipc_label) {
                    shared_ipc.callback(CALLBACK_IPC, 0, &json!({"webviewLabel":ipc_label,"windowLabel":ipc_window,"source":source,"documentId":document_id,"json":payload}), &[]);
                }
            })
            .with_on_page_load_handler(move |event, _url| {
                if matches!(event, PageLoadEvent::Started) {
                    let id = shared_page.next_document_id(); page_doc.store(id, Ordering::Release);
                    #[cfg(target_os = "linux")]
                    {
                        let result = page_ipc.lock().map_err(|_| "document IPC lock is poisoned".to_string())
                            .and_then(|mut ipc| ipc.commit(id, &_url));
                        if let Err(error) = result {
                            shared_page.callback(super::CALLBACK_FATAL, 0, &json!({"error":error}), &[]);
                            return;
                        }
                        let _ = shared_page.enqueue(super::UserEvent::ActivateIpc { label: page_label.clone(), document_id: id });
                    }
                    #[cfg(target_os = "linux")]
                    let _ = shared_page.enqueue(super::UserEvent::DialogDocumentChanged { webview_label: page_label.clone(), document_id: id });
                    shared_page.callback(CALLBACK_DOCUMENT, 0, &json!({"webviewLabel":page_label,"documentId":id,"closed":false}), &[]);
                }
            });
        if register_uri_scheme_protocols {
            for scheme in &self.uri_scheme_protocols {
                let shared = self.shared.clone();
                #[cfg(target_os = "linux")]
                let is_ipc = scheme == "ipc";
                builder = builder.with_asynchronous_custom_protocol(
                    scheme.clone(),
                    move |webview_id, request, responder| {
                        #[cfg(target_os = "linux")]
                        if is_ipc { shared.request_ipc_asset(webview_id, request, responder); return; }
                        shared.request_asset(webview_id, request, responder)
                    },
                );
            }
        }
        if register_tauri_protocol {
            builder = builder.with_asynchronous_custom_protocol(
                "tauri".into(),
                move |webview_id, request, responder| {
                    shared_tauri_asset.request_asset(webview_id, request, responder)
                },
            );
        }
        if register_asset_protocol {
            builder = builder.with_asynchronous_custom_protocol(
                "asset".into(),
                move |webview_id, request, responder| {
                    shared_file_asset.request_asset(webview_id, request, responder)
                },
            );
        }
        if register_isolation_protocol {
            builder = builder.with_asynchronous_custom_protocol(
                self.isolation_protocol
                    .clone()
                    .expect("isolation protocol was checked"),
                move |webview_id, request, responder| {
                    shared_isolation_asset.request_asset(webview_id, request, responder)
                },
            );
        }
        if register_ipc_protocol {
            builder = builder.with_asynchronous_custom_protocol(
                "ipc".into(),
                move |webview_id, request, responder| {
                    #[cfg(target_os = "linux")]
                    shared_ipc_protocol.request_ipc_asset(webview_id, request, responder);
                    #[cfg(not(target_os = "linux"))]
                    shared_ipc_protocol.request_asset(webview_id, request, responder)
                },
            );
        }
        if let Some(proxy) = parse_proxy_url(config.extra_config.get("proxyUrl"))? {
            builder = builder.with_proxy_config(proxy);
        }
        if drag_drop_enabled {
            let shared_drag = self.shared.clone();
            let drag_label = config.label.clone();
            builder = builder.with_drag_drop_handler(move |event| {
                let (event_name, payload) = match event {
                    DragDropEvent::Enter { paths, position } => (
                        "tauri://drag-enter",
                        json!({
                            "paths":paths.iter().map(|path| path.to_string_lossy()).collect::<Vec<_>>(),
                            "position":{"x":position.0,"y":position.1}
                        }),
                    ),
                    DragDropEvent::Over { position } => (
                        "tauri://drag-over",
                        json!({"position":{"x":position.0,"y":position.1}}),
                    ),
                    DragDropEvent::Drop { paths, position } => (
                        "tauri://drag-drop",
                        json!({
                            "paths":paths.iter().map(|path| path.to_string_lossy()).collect::<Vec<_>>(),
                            "position":{"x":position.0,"y":position.1}
                        }),
                    ),
                    DragDropEvent::Leave => ("tauri://drag-leave", Value::Null),
                    _ => return true,
                };
                shared_drag.callback(
                    CALLBACK_EVENT,
                    0,
                    &json!({"eventName":event_name,"label":drag_label,"payload":payload,"target":{"kind":"Webview","label":drag_label}}),
                    &[],
                );
                true
            });
        }
        if let Some(value) = config.extra_config.get("userAgent").and_then(Value::as_str) {
            builder = builder.with_user_agent(value);
        }
        if let Some(value) = transparent {
            builder = builder.with_transparent(value);
        }
        if let Some(value) = visible {
            builder = builder.with_visible(value);
        }
        if let Some(value) = incognito {
            builder = builder.with_incognito(value);
        }
        #[cfg(not(target_os = "windows"))]
        if let Some(value) = focus {
            builder = builder.with_focused(value);
        }
        // WRY 0.55.1 propagates initial MoveFocus failures as creation failures.
        // A minimized parent can reject focus even though its controller is valid.
        // Match upstream wry#1799: creation focus is best effort; explicit focus
        // commands below still report errors to the caller.
        #[cfg(target_os = "windows")]
        { builder = builder.with_focused(false); }
        if let Some(value) = accept_first_mouse {
            builder = builder.with_accept_first_mouse(value);
        }
        if let Some(value) = general_autofill_enabled {
            builder = builder.with_general_autofill_enabled(value);
        }
        if javascript_disabled {
            builder = builder.with_javascript_disabled();
        }
        if let Some(color) = optional_color_arg(&config.extra_config, "backgroundColor")? {
            builder = builder.with_background_color(color);
        }
        if let Some(value) = zoom_hotkeys_enabled {
            builder = builder.with_hotkeys_zoom(value);
        }
        #[cfg(target_os = "macos")]
        {
            use wry::WebViewBuilderExtDarwin;
            if let Some(value) = config.extra_config.get("dataStoreIdentifier").filter(|value| !value.is_null()) {
                let identifier: [u8; 16] = serde_json::from_value(value.clone()).map_err(|e| e.to_string())?;
                builder = builder.with_data_store_identifier(identifier);
            }
            if let Some(value) = optional_bool(&config.extra_config, "allowLinkPreview")? {
                builder = builder.with_allow_link_preview(value);
            }
            if let Some(position) = config.extra_config.get("trafficLightPosition").filter(|value| !value.is_null()) {
                let x = position["x"].as_f64().ok_or("trafficLightPosition requires x")?;
                let y = position["y"].as_f64().ok_or("trafficLightPosition requires y")?;
                builder = builder.with_traffic_light_inset(LogicalPosition::new(x, y));
            }
            if let Some(value) = config.extra_config.get("backgroundThrottling").filter(|value| !value.is_null()) {
                use tauri_utils::config::BackgroundThrottlingPolicy as Policy;
                let policy: Policy = serde_json::from_value(value.clone()).map_err(|e| e.to_string())?;
                builder = builder.with_background_throttling(match policy {
                    Policy::Disabled => wry::BackgroundThrottlingPolicy::Disabled,
                    Policy::Suspend => wry::BackgroundThrottlingPolicy::Suspend,
                    Policy::Throttle => wry::BackgroundThrottlingPolicy::Throttle,
                });
            }
        }
        if let Some(value) = devtools {
            builder = builder.with_devtools(value);
        }
        #[cfg(target_os = "windows")]
        {
            use wry::{ScrollBarStyle, WebViewBuilderExtWindows};
            if let Some(value) = config
                .extra_config
                .get("additionalBrowserArgs")
                .and_then(Value::as_str)
            {
                builder = builder.with_additional_browser_args(value);
            }
            if let Some(value) = optional_bool(&config.extra_config, "browserExtensionsEnabled")? {
                builder = builder.with_browser_extensions_enabled(value);
            }
            if use_https_scheme {
                builder = builder.with_https_scheme(true);
            }
            if config
                .extra_config
                .get("scrollBarStyle")
                .and_then(Value::as_str)
                == Some("fluentOverlay")
            {
                builder = builder.with_scroll_bar_style(ScrollBarStyle::FluentOverlay);
            }
        }
        self.shared.register_webview_origin(
            &config.label,
            window_origin,
            window.label.clone(),
            doc.clone(),
            #[cfg(target_os = "linux")]
            ipc.clone(),
        )?;
        #[cfg(target_os = "linux")]
        let native = {
            use tao::platform::unix::WindowExtUnix;
            use wry::WebViewBuilderExtUnix;
            // Pinned Tauri uses tao's box for both roots and children, including
            // menu-bar allocation. GTK owns their layout on X11 and Wayland.
            builder.build_gtk(
                window.window.default_vbox().ok_or("tao window did not create its GTK container")?,
            )
        };
        #[cfg(not(target_os = "linux"))]
        let native = builder.build_as_child(&window.window);
        let native =
            native.map_err(|e| format!("failed to create webview '{}': {e}", config.label));
        if native.is_err() {
            self.shared.remove_webview_origin(&config.label);
        }
        let context_is_unused = native.is_err() && context.webviews.is_empty();
        if native.is_ok() {
            context.webviews.insert(config.label.clone());
            if register_uri_scheme_protocols {
                context.uri_scheme_protocols_registered = true;
            }
            if register_tauri_protocol {
                context.tauri_protocol_registered = true;
            }
            if register_asset_protocol {
                context.asset_protocol_registered = true;
            }
            if register_isolation_protocol {
                context.isolation_protocol_registered = true;
            }
            if register_ipc_protocol {
                context.ipc_protocol_registered = true;
            }
        }
        let _ = context;
        if context_is_unused {
            self.web_contexts.remove(&context_key);
        }
        let native = native?;
        #[cfg(target_os = "linux")]
        {
            use wry::WebViewExtUnix;
            use webkit2gtk::WebViewExt;
            let gtk_view = native.webview();
            let load_ipc = ipc.clone();
            let load_shared = self.shared.clone();
            let load_label = config.label.clone();
            let load_doc = doc.clone();
            let failed_shared = self.shared.clone();
            let failed_label = config.label.clone();
            let failed_doc = doc.clone();
            let failed_ipc = ipc.clone();
            gtk_view.connect_load_failed(move |_, _, _, _| {
                let Ok(ipc) = failed_ipc.lock() else { return false; };
                let _ = failed_shared.enqueue(super::UserEvent::ResumeIpc {
                    label: failed_label.clone(), document_id: failed_doc.load(Ordering::Acquire), navigation: ipc.navigation(),
                });
                false
            });
            gtk_view.connect_load_changed(move |view, event| {
                if event == webkit2gtk::LoadEvent::Started {
                    if let Ok(mut ipc) = load_ipc.lock() { ipc.start_navigation(); }
                } else if event == webkit2gtk::LoadEvent::Finished {
                    if let Some(source) = view.uri() {
                        if let Ok(mut ipc) = load_ipc.lock() { ipc.finish_loading_source(source.as_str()); }
                    }
                    let _ = load_shared.enqueue(super::UserEvent::ActivateIpc {
                        label: load_label.clone(), document_id: load_doc.load(Ordering::Acquire),
                    });
                }
            });
            gtk_view.connect_uri_notify(move |view| {
                // WebKit publishes the candidate URI before LoadEvent::Started.
                // Only a committed page's idle history changes may update source.
                if view.is_loading() { return; }
                if let Some(source) = view.uri() {
                    if let Ok(mut ipc) = ipc.lock() { ipc.update_source(source.as_str()); }
                }
            });
        }
        #[cfg(target_os = "windows")]
        if focus.unwrap_or(true) {
            let _ = native.focus();
        }
        window.webviews.push(config.label.clone());
        self.webviews.insert(
            config.label.clone(),
            WebviewState {
                native,
                window_label: window.label.clone(),
                document_id: doc,
                auto_resize,
                context_key,
            },
        );
        if dynamic {
            self.event(
                "tauri://webview-created",
                Some(&config.label),
                json!({"label":config.label,"windowLabel":window.label}),
                "Webview",
            );
        }
        Ok(())
    }

    pub(super) fn create_child_webview(&mut self, submission: &Submission) -> Result<(), String> {
        let options = submission.args.get("options").unwrap_or(&submission.args);
        let config = parse_child_webview_options(options, submission.is_backend)?;
        let window_label = submission
            .args
            .get("windowLabel")
            .and_then(Value::as_str)
            .or(submission.window_label.as_deref())
            .or_else(|| {
                self.webviews
                    .get(&submission.webview_label)
                    .map(|v| v.window_label.as_str())
            })
            .ok_or_else(|| "create_webview has no parent window".to_string())?
            .to_owned();
        let mut window = self
            .windows
            .remove(&window_label)
            .ok_or_else(|| "window not found".to_string())?;
        let result = self.create_webview_in(&mut window, config, true, false);
        self.windows.insert(window_label, window);
        result
    }

    pub(super) fn invoke_webview(&mut self, command: &str, submission: &Submission) -> Result<Value, String> {
        let label = submission
            .args
            .get("label")
            .and_then(Value::as_str)
            .unwrap_or(&submission.webview_label)
            .to_owned();
        if command == "plugin:webview|webview_close" {
            self.remove_webview(&label)?;
            return Ok(Value::Null);
        }
        if command == "plugin:webview|reparent" {
            return self.reparent_webview(&label, required_string(&submission.args, "window")?);
        }
        let view = self
            .webviews
            .get_mut(&label)
            .ok_or_else(|| "webview not found".to_string())?;
        match command {
            "webview.navigate" => {
                view.native
                    .load_url(required_string(&submission.args, "url")?)
                    .map_err(|e| e.to_string())?;
                Ok(Value::Null)
            }
            "webview.postMessage" => {
                let value = submission.args.get("message").unwrap_or(&submission.args);
                view.native
                    .evaluate_script(&format!(
                        "window.dispatchEvent(new MessageEvent('message',{{data:{}}}))",
                        value
                    ))
                    .map_err(|e| e.to_string())?;
                Ok(Value::Null)
            }
            "plugin:webview|print" => {
                view.native.print().map_err(|e| e.to_string())?;
                Ok(Value::Null)
            }
            "plugin:webview|clear_all_browsing_data" => {
                view.native
                    .clear_all_browsing_data()
                    .map_err(|e| e.to_string())?;
                Ok(Value::Null)
            }
            "plugin:webview|set_webview_background_color" => {
                view.native
                    .set_background_color(
                        optional_color_arg(&submission.args, "value")
                            .map_err(|error| {
                                invalid_argument("set_webview_background_color", "value", error)
                            })?
                            .unwrap_or((255, 255, 255, 255)),
                    )
                    .map_err(|e| e.to_string())?;
                Ok(Value::Null)
            }
            "plugin:webview|internal_toggle_devtools" => {
                #[cfg(debug_assertions)]
                {
                    if view.native.is_devtools_open() {
                        view.native.close_devtools();
                    } else {
                        view.native.open_devtools();
                    }
                    Ok(Value::Null)
                }
                #[cfg(not(debug_assertions))]
                {
                    Err("devtools are disabled in this native library build".into())
                }
            }
            "plugin:webview|set_webview_focus" => {
                view.native.focus().map_err(|e| e.to_string())?;
                Ok(Value::Null)
            }
            "plugin:webview|webview_hide" => {
                view.native.set_visible(false).map_err(|e| e.to_string())?;
                Ok(Value::Null)
            }
            "plugin:webview|webview_show" => {
                view.native.set_visible(true).map_err(|e| e.to_string())?;
                Ok(Value::Null)
            }
            "plugin:webview|set_webview_zoom" => {
                view.native
                    .zoom(f64_arg(&submission.args, "value")?)
                    .map_err(|e| e.to_string())?;
                Ok(Value::Null)
            }
            "plugin:webview|webview_size" => {
                let scale_factor = self
                    .windows
                    .get(&view.window_label)
                    .ok_or_else(|| "window not found".to_string())?
                    .window
                    .scale_factor();
                let bounds = view.native.bounds().map_err(|e| e.to_string())?;
                Ok(size_json(bounds, scale_factor))
            }
            "plugin:webview|webview_position" => {
                let scale_factor = self
                    .windows
                    .get(&view.window_label)
                    .ok_or_else(|| "window not found".to_string())?
                    .window
                    .scale_factor();
                let bounds = view.native.bounds().map_err(|e| e.to_string())?;
                Ok(position_json(bounds, scale_factor))
            }
            "plugin:webview|set_webview_size" => {
                let (w, h, logical) = size_value(
                    submission
                        .args
                        .get("value")
                        .ok_or("set_webview_size requires value")?,
                )
                .map_err(|error| invalid_argument("set_webview_size", "value", error))?;
                let mut bounds = view.native.bounds().map_err(|e| e.to_string())?;
                bounds.size = if logical {
                    LogicalSize::new(w, h).into()
                } else {
                    tao::dpi::PhysicalSize::new(w as u32, h as u32).into()
                };
                if let Some(relative) = &mut view.auto_resize {
                    let parent = &self.windows
                        .get(&view.window_label)
                        .ok_or("window not found")?.window;
                    relative.set_size(bounds.size, parent.inner_size(), parent.scale_factor());
                }
                view.native
                    .set_bounds(bounds)
                    .map_err(|e| e.to_string())?;
                Ok(Value::Null)
            }
            "plugin:webview|set_webview_position" => {
                let (x, y, logical) = position_value(
                    submission
                        .args
                        .get("value")
                        .ok_or("set_webview_position requires value")?,
                )
                .map_err(|error| invalid_argument("set_webview_position", "value", error))?;
                let mut bounds = view.native.bounds().map_err(|e| e.to_string())?;
                bounds.position = if logical {
                    LogicalPosition::new(x, y).into()
                } else {
                    tao::dpi::PhysicalPosition::new(x as i32, y as i32).into()
                };
                if let Some(relative) = &mut view.auto_resize {
                    let parent = &self.windows
                        .get(&view.window_label)
                        .ok_or("window not found")?.window;
                    relative.set_position(bounds.position, parent.inner_size(), parent.scale_factor());
                }
                view.native
                    .set_bounds(bounds)
                    .map_err(|e| e.to_string())?;
                Ok(Value::Null)
            }
            "plugin:webview|set_webview_auto_resize" => {
                let enabled = bool_arg(&submission.args, "value")?;
                let bounds = view.native.bounds().map_err(|e| e.to_string())?;
                let parent = &self.windows
                    .get(&view.window_label)
                    .ok_or("window not found")?.window;
                view.auto_resize = enabled.then(|| {
                    RelativeBounds::new(bounds, parent.inner_size(), parent.scale_factor())
                });
                Ok(Value::Null)
            }
            _ => Err(format!(
                "native webview command '{command}' is not implemented"
            )),
        }
    }

    pub(super) fn eval(&self, label: &str, document_id: u64, script: &str) -> Result<(), String> {
        let view = self.webviews.get(label);
        let current = view.map(|view| view.document_id.load(Ordering::Acquire));
        if stale_eval_document(current, document_id) {
            return Ok(());
        }
        let view = view.ok_or_else(|| "webview not found".to_string())?;
        #[cfg(target_os = "linux")]
        let guarded = if document_id != 0 { self.guard_document_eval(label, script)? } else { None };
        #[cfg(target_os = "linux")]
        let script = if document_id != 0 {
            let Some(guarded) = guarded.as_deref() else { return Ok(()); };
            guarded
        } else { script };
        view.native
            .evaluate_script(script)
            .map_err(|e| format!("script evaluation failed for '{label}': {e}"))
    }

    pub(super) fn resize_auto_webviews(
        &mut self,
        window_label: &str,
        width: u32,
        height: u32,
    ) -> Result<(), String> {
        let Some(window) = self.windows.get(window_label) else {
            return Ok(());
        };
        let scale = window.window.scale_factor();
        for label in &window.webviews {
            if let Some(view) = self.webviews.get_mut(label)
                && let Some(relative) = view.auto_resize
            {
                view.native
                    .set_bounds(relative.bounds(tao::dpi::PhysicalSize::new(width, height), scale))
                    .map_err(|e| e.to_string())?;
            }
        }
        Ok(())
    }

    fn reparent_webview(&mut self, label: &str, destination: &str) -> Result<Value, String> {
        let old = self
            .webviews
            .get(label)
            .ok_or_else(|| "webview not found".to_string())?
            .window_label
            .clone();
        if old == destination {
            return Ok(Value::Null);
        }
        let Some(window) = self.windows.get(destination) else {
            return Ok(Value::Null);
        };
        let view = self.webviews.get_mut(label).expect("checked above");
        #[cfg(target_os = "windows")]
        {
            use tao::platform::windows::WindowExtWindows;
            use wry::WebViewExtWindows;
            view.native
                .reparent(window.window.hwnd())
                .map_err(|e| e.to_string())?;
        }
        #[cfg(target_os = "linux")]
        {
            use tao::platform::unix::WindowExtUnix;
            use wry::WebViewExtUnix;
            view.native
                .reparent(
                    window.window.default_vbox().ok_or("tao window did not create its GTK container")?,
                )
                .map_err(|e| e.to_string())?;
        }
        #[cfg(target_os = "macos")]
        {
            use tao::platform::macos::WindowExtMacOS;
            use wry::WebViewExtMacOS;
            view.native.reparent(window.window.ns_window().cast()).map_err(|e| e.to_string())?;
        }
        view.window_label = destination.to_owned();
        if let Some(window) = self.windows.get_mut(&old) {
            window.webviews.retain(|v| v != label);
        }
        self.windows
            .get_mut(destination)
            .expect("checked above")
            .webviews
            .push(label.to_owned());
        self.shared.reparent_document(label, destination)?;
        if self.track_webview_parents {
            self.shared.callback(CALLBACK_EVENT, 0, &json!({"eventName":"netwiv://webview-reparented","label":label,"payload":{"windowLabel":destination}}), &[]);
        }
        Ok(Value::Null)
    }
}

pub(super) fn stale_eval_document(current: Option<u64>, requested: u64) -> bool {
    requested != 0 && current != Some(requested)
}
