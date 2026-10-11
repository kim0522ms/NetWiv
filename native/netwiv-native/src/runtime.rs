use std::{
    collections::HashMap,
    ffi::c_void,
    path::{Component, Path, PathBuf},
    sync::{
        Arc, Mutex,
        atomic::{AtomicU8, AtomicU64, Ordering},
    },
    time::{Duration, Instant},
};

use http::{HeaderName, HeaderValue, Response};
use serde::Deserialize;
use serde_json::{Value, json};
use tao::{
    dpi::{LogicalPosition, LogicalSize, LogicalUnit, PhysicalUnit, PixelUnit, Position, Size},
    event::{Event, WindowEvent},
    event_loop::{ControlFlow, EventLoopBuilder, EventLoopProxy, EventLoopWindowTarget},
    monitor::MonitorHandle,
    platform::run_return::EventLoopExtRunReturn,
    window::{
        CursorIcon, Fullscreen, ProgressBarState, ProgressState, ResizeDirection, Theme,
        UserAttentionType, Window, WindowBuilder, WindowId, WindowSizeConstraints,
    },
};
use tauri_utils::config::{Color as TauriColor, WindowConfig as TauriWindowConfig};
use wry::{
    DragDropEvent, PageLoadEvent, ProxyConfig, ProxyEndpoint, Rect, RequestAsyncResponder,
    WebContext, WebView, WebViewBuilder,
};

use crate::{
    abi::NetWivCallback,
    model::{
        AssetResponseMetadata, RunConfig, Submission, WebviewConfig, WindowConfig, bool_arg,
        f64_arg, required_string, resolve_url, validate_config, validate_runtime_label,
    },
    relative_bounds::RelativeBounds,
    resources::{ChannelTarget, NativeResources},
};

pub(crate) const STATE_CREATED: u8 = 0;
pub(crate) const STATE_RUNNING: u8 = 1;
const STATE_STOPPED: u8 = 2;

const CALLBACK_IPC: u32 = 1;
const CALLBACK_EVENT: u32 = 2;
const CALLBACK_COMPLETION: u32 = 3;
const CALLBACK_DISPATCH: u32 = 4;
const CALLBACK_ASSET: u32 = 5;
const CALLBACK_DOCUMENT: u32 = 6;
const CALLBACK_FATAL: u32 = 7;
const CALLBACK_READY: u32 = 8;

pub struct RuntimeHandle {
    pub(crate) shared: Arc<Shared>,
}

pub(crate) struct Shared {
    callback: NetWivCallback,
    user: usize,
    pub state: AtomicU8,
    proxy: Mutex<Option<EventLoopProxy<UserEvent>>>,
    assets: Mutex<HashMap<u64, RequestAsyncResponder>>,
    webview_origins: Mutex<HashMap<String, ProtocolDocument>>,
    next_asset: AtomicU64,
    next_document: AtomicU64,
}

struct ProtocolDocument {
    origin: String,
    window_label: String,
    document_id: Arc<AtomicU64>,
}

impl Shared {
    pub(crate) fn new(callback: NetWivCallback, user: *mut c_void) -> Self {
        Self {
            callback,
            user: user as usize,
            state: AtomicU8::new(STATE_CREATED),
            proxy: Mutex::new(None),
            assets: Mutex::new(HashMap::new()),
            webview_origins: Mutex::new(HashMap::new()),
            next_asset: AtomicU64::new(1),
            next_document: AtomicU64::new(1),
        }
    }

    pub(crate) fn enqueue(&self, event: UserEvent) -> Result<(), String> {
        if self.state.load(Ordering::Acquire) != STATE_RUNNING {
            return Err("native runtime is not running".into());
        }
        let proxy = self
            .proxy
            .lock()
            .map_err(|_| "native event proxy lock is poisoned".to_string())?
            .clone()
            .ok_or_else(|| "native event loop is not ready".to_string())?;
        proxy
            .send_event(event)
            .map_err(|_| "native event loop has stopped".into())
    }

    fn callback(&self, kind: u32, id: u64, json: &Value, binary: &[u8]) -> i32 {
        let bytes = serde_json::to_vec(json).unwrap_or_else(|_| b"null".to_vec());
        let binary_ptr = if binary.is_empty() {
            std::ptr::null()
        } else {
            binary.as_ptr()
        };
        // SAFETY: callback ownership and borrowed-span lifetime are fixed by the ABI contract.
        unsafe {
            (self.callback)(
                self.user as *mut c_void,
                kind,
                id,
                bytes.as_ptr(),
                bytes.len(),
                binary_ptr,
                binary.len(),
            )
        }
    }

    fn next_document_id(&self) -> u64 {
        self.next_document.fetch_add(1, Ordering::Relaxed)
    }

    fn register_webview_origin(
        &self,
        label: &str,
        origin: String,
        window_label: String,
        document_id: Arc<AtomicU64>,
    ) -> Result<(), String> {
        self.webview_origins
            .lock()
            .map_err(|_| "webview origin lock is poisoned".to_string())?
            .insert(
                label.to_string(),
                ProtocolDocument {
                    origin,
                    window_label,
                    document_id,
                },
            );
        Ok(())
    }

    fn remove_webview_origin(&self, label: &str) {
        if let Ok(mut origins) = self.webview_origins.lock() {
            origins.remove(label);
        }
    }

    fn webview_parent(&self, label: &str) -> Option<String> {
        self.webview_origins
            .lock()
            .ok()?
            .get(label)
            .map(|document| document.window_label.clone())
    }

    fn reparent_document(&self, label: &str, destination: &str) -> Result<(), String> {
        let mut origins = self
            .webview_origins
            .lock()
            .map_err(|_| "webview origin lock is poisoned".to_string())?;
        let document = origins
            .get_mut(label)
            .ok_or_else(|| "webview not found".to_string())?;
        document.window_label = destination.to_owned();
        Ok(())
    }

    fn request_asset(
        &self,
        webview_id: &str,
        request: http::Request<Vec<u8>>,
        responder: RequestAsyncResponder,
    ) {
        let id = self.next_asset.fetch_add(1, Ordering::Relaxed);
        let headers: serde_json::Map<String, Value> = request
            .headers()
            .iter()
            .filter_map(|(name, value)| {
                value
                    .to_str()
                    .ok()
                    .map(|value| (name.as_str().to_owned(), json!(value)))
            })
            .collect();
        if let Ok(mut assets) = self.assets.lock() {
            assets.insert(id, responder);
        } else {
            return;
        }
        let (window_origin, window_label, document_id) = self
            .webview_origins
            .lock()
            .ok()
            .and_then(|origins| {
                origins.get(webview_id).map(|document| {
                    (
                        document.origin.clone(),
                        document.window_label.clone(),
                        document.document_id.load(Ordering::Acquire),
                    )
                })
            })
            .unwrap_or_else(|| ("null".to_string(), String::new(), 0));
        let payload = json!({"uri": request.uri().to_string(), "method": request.method().as_str(), "headers": headers, "windowOrigin": window_origin, "webviewLabel":webview_id, "windowLabel":window_label, "documentId":document_id});
        self.callback(CALLBACK_ASSET, id, &payload, request.body());
    }

    pub(crate) fn respond_asset(
        &self,
        id: u64,
        metadata: &str,
        body: Vec<u8>,
    ) -> Result<(), String> {
        // Async managed commands may finish after run drained every responder.
        if self.state.load(Ordering::Acquire) == STATE_STOPPED {
            return Ok(());
        }
        if !self.assets.lock().map_err(|_| "asset responder lock is poisoned".to_string())?.contains_key(&id) {
            return Err(format!("no pending asset request has id {id}"));
        }
        let (response, error) = match build_asset_response(metadata, body) {
            Ok(response) => (response, None),
            Err(error) => (
                Response::builder().status(500).header("content-type", "text/plain; charset=utf-8")
                    .body(format!("invalid NetWiv asset response: {error}").into_bytes())
                    .expect("static error response is valid"),
                Some(error),
            ),
        };
        // WKWebView's responder synchronously dispatches to AppKit off-thread.
        // Queue delivery so an exported call never waits for the UI while the
        // managed runtime holds its lifetime lock.
        self.enqueue(UserEvent::AssetResponse { id, response })?;
        error.map_or(Ok(()), Err)
    }

    fn drain_assets(&self) {
        let responders = self
            .assets
            .lock()
            .map(|mut pending| std::mem::take(&mut *pending))
            .unwrap_or_default();
        for (_, responder) in responders {
            responder.respond(
                Response::builder()
                    .status(503)
                    .header("content-type", "text/plain; charset=utf-8")
                    .body(b"NetWiv runtime stopped".to_vec())
                    .expect("static response is valid"),
            );
        }
    }
}

fn build_asset_response(metadata: &str, body: Vec<u8>) -> Result<Response<Vec<u8>>, String> {
    let metadata: AssetResponseMetadata = serde_json::from_str(metadata)
        .map_err(|e| format!("invalid asset response metadata: {e}"))?;
    if !(100..=599).contains(&metadata.status_code) {
        return Err("asset statusCode must be between 100 and 599".into());
    }
    let mut builder = Response::builder().status(metadata.status_code);
    if !metadata.content_type.trim().is_empty() {
        builder = builder.header("content-type", metadata.content_type);
    }
    for (name, value) in metadata.headers {
        let name = HeaderName::try_from(name)
            .map_err(|e| format!("invalid asset response header name: {e}"))?;
        let value = HeaderValue::try_from(value)
            .map_err(|e| format!("invalid asset response header value: {e}"))?;
        builder = builder.header(name, value);
    }
    builder
        .body(body)
        .map_err(|e| format!("invalid asset response: {e}"))
}

pub(crate) enum UserEvent {
    AssetResponse { id: u64, response: Response<Vec<u8>> },
    Submit {
        request_id: u64,
        json: String,
        binary: Vec<u8>,
    },
    Dispatch {
        request_id: u64,
    },
    Eval {
        label: String,
        document_id: u64,
        script: String,
    },
    Exit {
        exit_code: i32,
    },
}

struct WindowState {
    window: Window,
    label: String,
    webviews: Vec<String>,
    enabled: bool,
    menu: Option<muda::Menu>,
    normal_size: tao::dpi::PhysicalSize<u32>,
    normal_position: tao::dpi::PhysicalPosition<i32>,
}

impl WindowState {
    fn capture_normal_bounds(&mut self) {
        if !self.window.is_maximized() && !self.window.is_minimized() && self.window.fullscreen().is_none() {
            let size = self.window.inner_size();
            if size.width > 0 && size.height > 0 { self.normal_size = size; }
            if let Ok(position) = self.window.outer_position() { self.normal_position = position; }
        }
    }

    fn persistence_snapshot(&mut self) -> Value {
        self.capture_normal_bounds();
        json!({"width":self.normal_size.width,"height":self.normal_size.height,
            "x":self.normal_position.x,"y":self.normal_position.y,
            "maximized":self.window.is_maximized(),"visible":self.window.is_visible(),
            "decorated":self.window.is_decorated(),"fullscreen":self.window.fullscreen().is_some()})
    }
}

struct WebviewState {
    native: WebView,
    window_label: String,
    document_id: Arc<AtomicU64>,
    auto_resize: Option<RelativeBounds>,
    context_key: Option<PathBuf>,
}

struct WebContextState {
    native: WebContext,
    webviews: std::collections::HashSet<String>,
    tauri_protocol_registered: bool,
    asset_protocol_registered: bool,
    isolation_protocol_registered: bool,
    ipc_protocol_registered: bool,
    uri_scheme_protocols_registered: bool,
}

struct LocalRuntime {
    shared: Arc<Shared>,
    initialization_script: String,
    local_data_directory: Option<PathBuf>,
    asset_protocol_enabled: bool,
    isolation_protocol: Option<String>,
    ipc_protocol_enabled: bool,
    uri_scheme_protocols: Vec<String>,
    track_webview_parents: bool,
    default_window_icon: Option<tao::window::Icon>,
    app_theme: Option<Theme>,
    app_menu: Option<muda::Menu>,
    #[cfg(target_os = "windows")]
    menu_accelerators: crate::menu_accelerators::MenuAccelerators,
    windows: HashMap<String, WindowState>,
    window_ids: HashMap<WindowId, String>,
    webviews: HashMap<String, WebviewState>,
    web_contexts: HashMap<Option<PathBuf>, WebContextState>,
    resources: NativeResources,
    clipboard: Option<arboard::Clipboard>,
    global_shortcuts: crate::global_shortcut::GlobalShortcuts,
    strongholds: crate::stronghold::Strongholds,
    exit_code: i32,
}

pub(crate) fn run(shared: Arc<Shared>, config: RunConfig) -> Result<(), String> {
    validate_config(&config)?;
    shared
        .state
        .compare_exchange(
            STATE_CREATED,
            STATE_RUNNING,
            Ordering::AcqRel,
            Ordering::Acquire,
        )
        .map_err(|_| "native runtime can only run once".to_string())?;

    let _guard = RunGuard(shared.clone());
    run_inner(shared, config)
}

struct RunGuard(Arc<Shared>);
impl Drop for RunGuard {
    fn drop(&mut self) {
        self.0.state.store(STATE_STOPPED, Ordering::Release);
        if let Ok(mut proxy) = self.0.proxy.lock() {
            *proxy = None;
        }
        self.0.drain_assets();
        if let Ok(mut origins) = self.0.webview_origins.lock() {
            origins.clear();
        }
    }
}

fn run_inner(shared: Arc<Shared>, config: RunConfig) -> Result<(), String> {
    #[cfg(target_os = "macos")]
    if objc2::MainThreadMarker::new().is_none() {
        return Err("The macOS event loop must run on the process main thread.".into());
    }
    let mut builder = EventLoopBuilder::<UserEvent>::with_user_event();
    #[cfg(target_os = "windows")]
    let menu_accelerators = crate::menu_accelerators::MenuAccelerators::default();
    #[cfg(target_os = "windows")]
    menu_accelerators.install(&mut builder);
    let mut event_loop = builder.build();
    *shared
        .proxy
        .lock()
        .map_err(|_| "native event proxy lock is poisoned".to_string())? =
        Some(event_loop.create_proxy());
    let resources = NativeResources::new();
    let default_window_icon = resources.window_icon(config.default_window_icon.as_ref())?;
    // Tauri's explicit webview `dataDirectory` uses LocalData. The app-scoped value is
    // carried in the run contract for Core-owned paths, but does not override Wry's
    // default (None-keyed) browser context.
    let _app_local_data_directory = config.app_local_data_directory;
    let mut runtime = LocalRuntime {
        shared: shared.clone(),
        initialization_script: config.initialization_script,
        local_data_directory: config.local_data_directory,
        asset_protocol_enabled: config.asset_protocol_enabled,
        isolation_protocol: config.isolation_protocol,
        ipc_protocol_enabled: config.ipc_protocol_enabled,
        uri_scheme_protocols: config.uri_scheme_protocols,
        track_webview_parents: config.track_webview_parents,
        default_window_icon,
        app_theme: None,
        app_menu: None,
        #[cfg(target_os = "windows")]
        menu_accelerators,
        windows: HashMap::new(),
        window_ids: HashMap::new(),
        webviews: HashMap::new(),
        web_contexts: HashMap::new(),
        resources,
        clipboard: None,
        global_shortcuts: crate::global_shortcut::GlobalShortcuts::new(),
        strongholds: crate::stronghold::Strongholds::new(),
        exit_code: 0,
    };
    #[cfg(target_os = "macos")]
    {
        let menu = runtime.resources.invoke_backend("plugin:menu|create_default", &json!({}), "", 0)?;
        let menu = runtime.resources.root_menu_clone(menu[0].as_u64().ok_or("invalid default menu resource")? as u32)?;
        menu.init_for_nsapp();
        runtime.app_menu = Some(menu);
    }
    for window in config.windows {
        runtime.create_window(&event_loop, window, false, true)?;
    }
    runtime.shared.callback(
        CALLBACK_READY,
        0,
        &json!({"backend": platform_backend(&event_loop)}),
        &[],
    );

    let platform_status = event_loop.run_return(|event, target, control_flow| {
        *control_flow = ControlFlow::WaitUntil(Instant::now() + Duration::from_millis(25));
        let result = runtime.handle_event(event, target, control_flow);
        if let Err(error) = result {
            runtime
                .shared
                .callback(CALLBACK_FATAL, 0, &json!({"error": error}), &[]);
            runtime.exit_code = 1;
            runtime.shutdown(control_flow);
        }
    });
    runtime.close_all_documents();
    runtime.webviews.clear();
    runtime.web_contexts.clear();
    runtime.windows.clear();
    if platform_status != 0 && runtime.exit_code == 0 {
        return Err(format!(
            "native event loop exited with platform status {platform_status}"
        ));
    }
    Ok(())
}

impl LocalRuntime {
    fn handle_event(
        &mut self,
        event: Event<'_, UserEvent>,
        target: &EventLoopWindowTarget<UserEvent>,
        control_flow: &mut ControlFlow,
    ) -> Result<(), String> {
        match event {
            Event::UserEvent(event) => self.handle_user_event(event, target, control_flow),
            Event::WindowEvent {
                window_id, event, ..
            } => self.handle_window_event(window_id, event, control_flow),
            Event::MainEventsCleared => {
                // GTK can report resize before its maximized state has settled.
                // Snapshot normal bounds after the native event batch instead.
                for state in self.windows.values_mut() { state.capture_normal_bounds(); }
                self.drain_native_events();
                Ok(())
            }
            _ => Ok(()),
        }
    }

    fn handle_user_event(
        &mut self,
        event: UserEvent,
        target: &EventLoopWindowTarget<UserEvent>,
        control_flow: &mut ControlFlow,
    ) -> Result<(), String> {
        match event {
            UserEvent::Submit {
                request_id,
                json,
                binary,
            } => {
                let had_windows = !self.windows.is_empty();
                let result = serde_json::from_str::<Submission>(&json)
                    .map_err(|e| format!("invalid submission: {e}"))
                    .and_then(|submission| self.invoke(target, &submission, &binary));
                match result {
                    Ok(InvokeResult::Json(value)) => {
                        self.shared.callback(
                            CALLBACK_COMPLETION,
                            request_id,
                            &json!({"ok":true,"value":value,"isBinary":false}),
                            &[],
                        );
                    }
                    Ok(InvokeResult::Binary(value)) => {
                        self.shared.callback(
                            CALLBACK_COMPLETION,
                            request_id,
                            &json!({"ok":true,"isBinary":true}),
                            &value,
                        );
                    }
                    Err(error) => {
                        self.shared.callback(
                            CALLBACK_COMPLETION,
                            request_id,
                            &json!({"ok":false,"error":error}),
                            &[],
                        );
                    }
                }
                if had_windows && self.windows.is_empty() {
                    self.shutdown(control_flow);
                }
                Ok(())
            }
            UserEvent::AssetResponse { id, response } => {
                let responder = self.shared.assets.lock()
                    .map_err(|_| "asset responder lock is poisoned".to_string())?.remove(&id);
                if let Some(responder) = responder { responder.respond(response); }
                Ok(())
            }
            UserEvent::Dispatch { request_id } => {
                self.shared
                    .callback(CALLBACK_DISPATCH, request_id, &Value::Null, &[]);
                Ok(())
            }
            UserEvent::Eval {
                label,
                document_id,
                script,
            } => self.eval(&label, document_id, &script),
            UserEvent::Exit { exit_code } => {
                self.exit_code = exit_code;
                self.shutdown(control_flow);
                Ok(())
            }
        }
    }

    fn handle_window_event(
        &mut self,
        window_id: WindowId,
        event: WindowEvent<'_>,
        control_flow: &mut ControlFlow,
    ) -> Result<(), String> {
        let Some(label) = self.window_ids.get(&window_id).cloned() else {
            return Ok(());
        };
        match event {
            WindowEvent::CloseRequested => {
                let prevent = self.shared.callback(CALLBACK_EVENT, 0, &json!({"eventName":"tauri://close-requested","label":label,"payload":null,"target":{"kind":"Window","label":label}}), &[]) == 1;
                if !prevent {
                    let _ = self.remove_window(&label);
                    if self.windows.is_empty() {
                        self.shutdown(control_flow);
                    }
                }
            }
            WindowEvent::Resized(size) => {
                self.resize_auto_webviews(&label, size.width, size.height)?;
                self.event(
                    "tauri://resize",
                    Some(&label),
                    json!({"width":size.width,"height":size.height}),
                    "Window",
                );
            }
            WindowEvent::Moved(position) => self.event(
                "tauri://move",
                Some(&label),
                json!({"x":position.x,"y":position.y}),
                "Window",
            ),
            WindowEvent::Focused(focused) => {
                #[cfg(target_os = "macos")]
                if focused {
                    if let Some(menu) = self.windows.get(&label).and_then(|w| w.menu.as_ref()).or(self.app_menu.as_ref()) {
                        menu.init_for_nsapp();
                    }
                }
                self.event(
                if focused {
                    "tauri://focus"
                } else {
                    "tauri://blur"
                },
                Some(&label),
                Value::Null,
                "Window",
            );
            },
            WindowEvent::ScaleFactorChanged {
                scale_factor,
                new_inner_size,
            } => self.event(
                "tauri://scale-change",
                Some(&label),
                json!({
                    "scaleFactor": scale_factor,
                    "size": {"width": new_inner_size.width, "height": new_inner_size.height}
                }),
                "Window",
            ),
            WindowEvent::ThemeChanged(theme) => self.event(
                "tauri://theme-changed",
                Some(&label),
                json!(theme_name(theme)),
                "Window",
            ),
            _ => {}
        }
        Ok(())
    }

    fn create_window(
        &mut self,
        target: &EventLoopWindowTarget<UserEvent>,
        config: WindowConfig,
        dynamic: bool,
        create_implicit_webview: bool,
    ) -> Result<(), String> {
        if self.windows.contains_key(&config.label) {
            return Err(format!("window '{}' already exists", config.label));
        }
        let extra = &config.extra_config;
        let prevent_overflow = prevent_overflow_margin(extra.get("preventOverflow"))?;
        let visible = optional_bool(extra, "visible")?.unwrap_or(true);
        let resizable = optional_bool(extra, "resizable")?.unwrap_or(true);
        let maximizable = optional_bool(extra, "maximizable")?.unwrap_or(true);
        let minimizable = optional_bool(extra, "minimizable")?.unwrap_or(true);
        let closable = optional_bool(extra, "closable")?.unwrap_or(true);
        let focus = optional_bool(extra, "focus")?.unwrap_or(true);
        let focusable = optional_bool(extra, "focusable")?.unwrap_or(true);
        let transparent = optional_bool(extra, "transparent")?.unwrap_or(false);
        let decorations = optional_bool(extra, "decorations")?.unwrap_or(true);
        let always_on_top = optional_bool(extra, "alwaysOnTop")?.unwrap_or(false);
        let always_on_bottom = optional_bool(extra, "alwaysOnBottom")?.unwrap_or(false);
        let visible_on_all_workspaces =
            optional_bool(extra, "visibleOnAllWorkspaces")?.unwrap_or(false);
        let content_protected = optional_bool(extra, "contentProtected")?.unwrap_or(false);
        let maximized = optional_bool(extra, "maximized")?.unwrap_or(false);
        let fullscreen = optional_bool(extra, "fullscreen")?.unwrap_or(false);
        let skip_taskbar = optional_bool(extra, "skipTaskbar")?.unwrap_or(false);
        let shadow = optional_bool(extra, "shadow")?.unwrap_or(true);
        let center = optional_bool(extra, "center")?.unwrap_or(false);
        let enabled = optional_bool(extra, "enabled")?.unwrap_or(true);
        let mut builder = WindowBuilder::new()
            .with_title(config.title.clone())
            .with_inner_size(LogicalSize::new(config.width, config.height))
            .with_window_icon(
                self.resources
                    .window_icon(extra.get("icon"))?
                    .or_else(|| self.default_window_icon.clone()),
            )
            .with_theme(parse_theme(extra.get("theme"))?.or(self.app_theme))
            .with_visible(visible)
            .with_resizable(resizable)
            .with_maximizable(maximizable)
            .with_minimizable(minimizable)
            .with_closable(closable)
            .with_focused(focus)
            .with_focusable(focusable)
            .with_transparent(transparent)
            .with_decorations(decorations)
            .with_always_on_top(always_on_top)
            .with_always_on_bottom(always_on_bottom)
            .with_visible_on_all_workspaces(visible_on_all_workspaces)
            .with_content_protection(content_protected)
            .with_maximized(maximized);
        if let Some(parent_label) = optional_string(extra, "parent")? {
            let parent = self
                .windows
                .get(parent_label)
                .ok_or_else(|| format!("no native parent window has label '{parent_label}'"))?;
            #[cfg(target_os = "windows")]
            {
                use tao::platform::windows::{WindowBuilderExtWindows, WindowExtWindows};
                builder = builder.with_owner_window(parent.window.hwnd());
            }
            #[cfg(target_os = "linux")]
            {
                use tao::platform::unix::{WindowBuilderExtUnix, WindowExtUnix};
                builder = builder.with_transient_for(parent.window.gtk_window());
            }
            #[cfg(target_os = "macos")]
            {
                use tao::platform::macos::{WindowBuilderExtMacOS, WindowExtMacOS};
                builder = builder.with_parent_window(parent.window.ns_window());
            }
        }
        #[cfg(target_os = "macos")]
        { builder = crate::macos::configure_window(builder, extra)?; }
        #[cfg(target_os = "windows")]
        if let Some(classname) = optional_string(extra, "windowClassname")? {
            use tao::platform::windows::WindowBuilderExtWindows;
            builder = builder.with_window_classname(classname);
        }
        if let (Some(x), Some(y)) = (
            extra.get("x").and_then(Value::as_f64),
            extra.get("y").and_then(Value::as_f64),
        ) {
            builder = builder.with_position(LogicalPosition::new(x, y));
        }
        let constraints = logical_window_constraints(extra)?;
        if constraints != WindowSizeConstraints::default() {
            builder = builder.with_inner_size_constraints(constraints);
        }
        if let Some(color) = optional_color_arg(extra, "backgroundColor")? {
            builder = builder.with_background_color(color);
        }
        if fullscreen {
            builder = builder.with_fullscreen(Some(Fullscreen::Borderless(None)));
        }
        let window = builder
            .build(target)
            .map_err(|e| format!("failed to create window '{}': {e}", config.label))?;
        if let Some(menu) = &self.app_menu {
            attach_menu(menu, &window)?;
        }
        if let Some(margin) = prevent_overflow {
            apply_prevent_overflow(&window, margin);
        }
        set_skip_taskbar(&window, skip_taskbar)?;
        set_shadow(&window, shadow)?;
        if let Some(effects) = extra.get("windowEffects") {
            set_window_effects(&window, Some(effects))?;
        }
        if center {
            center_window(&window);
        }
        if !enabled {
            set_window_enabled(&window, false);
        }
        let id = window.id();
        let label = config.label.clone();
        let is_webview_window = create_implicit_webview && config.webviews.is_none();
        let mut state = WindowState {
            normal_size: window.inner_size(),
            normal_position: window.outer_position().unwrap_or_default(),
            window,
            label: label.clone(),
            webviews: Vec::new(),
            enabled,
            menu: None,
        };
        let webviews = config.webviews.unwrap_or_else(|| {
            if create_implicit_webview {
                vec![WebviewConfig {
                    label: label.clone(),
                    url: config.url,
                    x: 0.0,
                    y: 0.0,
                    width: config.width,
                    height: config.height,
                    extra_config: config.extra_config,
                }]
            } else {
                Vec::new()
            }
        });
        for webview in webviews {
            self.create_webview_in(&mut state, webview, dynamic, is_webview_window)?;
        }
        self.window_ids.insert(id, label.clone());
        self.windows.insert(label.clone(), state);
        if dynamic {
            self.event(
                "tauri://window-created",
                Some(&label),
                json!({"label":label,"isWebviewWindow":is_webview_window}),
                "Window",
            );
        }
        Ok(())
    }

    fn create_webview_in(
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
        let shared_page = self.shared.clone();
        let page_label = config.label.clone();
        let page_doc = doc.clone();
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
            // IPC authorizes the browser-supplied Origin, never guest JSON.
            .with_initialization_script_for_main_only(bridge, !cfg!(target_os = "windows"))
            .with_initialization_script_for_main_only(self.initialization_script.clone(), !cfg!(target_os = "windows"))
            .with_ipc_handler(move |request| {
                let source = request.uri().to_string();
                let document_id = ipc_doc.load(Ordering::Acquire);
                if let Some(ipc_window) = shared_ipc.webview_parent(&ipc_label) {
                    shared_ipc.callback(CALLBACK_IPC, 0, &json!({"webviewLabel":ipc_label,"windowLabel":ipc_window,"source":source,"documentId":document_id,"json":request.body()}), &[]);
                }
            })
            .with_on_page_load_handler(move |event, _url| {
                if matches!(event, PageLoadEvent::Started) {
                    let id = shared_page.next_document_id(); page_doc.store(id, Ordering::Release);
                    shared_page.callback(CALLBACK_DOCUMENT, 0, &json!({"webviewLabel":page_label,"documentId":id,"closed":false}), &[]);
                }
            });
        if register_uri_scheme_protocols {
            for scheme in &self.uri_scheme_protocols {
                let shared = self.shared.clone();
                builder = builder.with_asynchronous_custom_protocol(
                    scheme.clone(),
                    move |webview_id, request, responder| {
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
        if let Some(value) = focus {
            builder = builder.with_focused(value);
        }
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

    fn invoke(
        &mut self,
        target: &EventLoopWindowTarget<UserEvent>,
        submission: &Submission,
        binary: &[u8],
    ) -> Result<InvokeResult, String> {
        let _ = (&submission.source, &submission.scope, binary);
        if !submission.is_authorized {
            return Err(format!(
                "native command '{}' was not authorized by Core",
                submission.command
            ));
        }
        if submission.document_id != 0 {
            let current = self
                .webviews
                .get(&submission.webview_label)
                .ok_or_else(|| {
                    format!(
                        "submission targets missing webview '{}'",
                        submission.webview_label
                    )
                })?
                .document_id
                .load(Ordering::Acquire);
            if current != submission.document_id {
                return Err(format!(
                    "submission targets stale document {} for webview '{}' (current document is {current})",
                    submission.document_id, submission.webview_label
                ));
            }
        }
        let command = submission.command.as_str();
        if matches!(command, "plugin:app|app_show" | "plugin:app|app_hide" | "plugin:app|set_dock_visibility") {
            #[cfg(target_os = "macos")]
            {
                use tao::platform::macos::EventLoopWindowTargetExtMacOS;
                match command {
                    "plugin:app|app_show" => target.show_application(),
                    "plugin:app|app_hide" => target.hide_application(),
                    _ => target.set_dock_visibility(bool_arg(&submission.args, "visible")?),
                }
            }
            return Ok(InvokeResult::Json(Value::Null));
        }
        if matches!(command, "plugin:app|fetch_data_store_identifiers" | "plugin:app|remove_data_store") {
            #[cfg(target_os = "macos")]
            return Err("WKWebsiteDataStore identifier management is not implemented by NetWiv on macOS.".into());
            #[cfg(not(target_os = "macos"))]
            return Ok(InvokeResult::Json(if command.ends_with("fetch_data_store_identifiers") { json!([]) } else { Value::Null }));
        }
        if command == "plugin:app|set_app_theme" {
            let theme = parse_theme(submission.args.get("theme"))?;
            self.app_theme = theme;
            for state in self.windows.values() {
                state.window.set_theme(theme);
            }
            return Ok(InvokeResult::Json(Value::Null));
        }
        if command == "plugin:path|resolve_directory" {
            return crate::paths::resolve(&submission.args).map(InvokeResult::Json);
        }
        if command.starts_with("plugin:dialog|") {
            let label = submission.window_label.as_deref()
                .or_else(|| self.webviews.get(&submission.webview_label).map(|view| view.window_label.as_str()))
                .ok_or("dialog request has no parent window")?;
            let window = self.windows.get(label).ok_or("dialog parent window was not found")?;
            return crate::dialog::invoke(command, &submission.args, &window.window).map(InvokeResult::Json);
        }
        if command.starts_with("plugin:clipboard-manager|") {
            return crate::clipboard::invoke(command, &submission.args, &mut self.clipboard, &mut self.resources)
                .map(InvokeResult::Json);
        }
        if command == "plugin:notification|notify" {
            return crate::notification::notify(&submission.args).map(InvokeResult::Json);
        }
        if command.starts_with("plugin:global-shortcut|") {
            return self.global_shortcuts.invoke(command, &submission.args).map(InvokeResult::Json);
        }
        if command.starts_with("plugin:stronghold|") {
            return self.strongholds.invoke(command, &submission.args).map(InvokeResult::Json);
        }
        if command.starts_with("plugin:menu|")
            || command.starts_with("plugin:tray|")
            || command.starts_with("plugin:image|")
            || command == "plugin:resources|close"
        {
            if command == "plugin:resources|close" {
                if let Some(target) = self
                    .resources
                    .take_channel(crate::model::rid_arg(&submission.args)?)?
                {
                    self.send_channel_end(target);
                }
                return Ok(InvokeResult::Json(Value::Null));
            }
            if command == "plugin:menu|set_as_window_menu" {
                return self
                    .set_window_menu(&submission.args, submission)
                    .map(InvokeResult::Json);
            }
            if command == "plugin:menu|set_as_app_menu" {
                return self.set_app_menu(&submission.args).map(InvokeResult::Json);
            }
            if command == "plugin:menu|popup" {
                return self
                    .popup_menu(&submission.args, submission)
                    .map(InvokeResult::Json);
            }
            if command == "plugin:tray|set_menu" {
                return self
                    .resources
                    .set_tray_menu(&submission.args)
                    .map(InvokeResult::Json);
            }
            let result = if submission.is_backend {
                self.resources.invoke_backend(
                    command,
                    &submission.args,
                    &submission.webview_label,
                    submission.document_id,
                )
            } else {
                self.resources.invoke(
                    command,
                    &submission.args,
                    &submission.webview_label,
                    submission.document_id,
                )
            };
            return result.map(InvokeResult::Json);
        }
        match command {
            "plugin:window|get_all_windows" => {
                Ok(InvokeResult::Json(json!(sorted(self.windows.keys()))))
            }
            "plugin:webview|get_all_webviews" => Ok(InvokeResult::Json(Value::Array(
                self.webviews
                    .iter()
                    .map(|(label, view)| json!({"window_label":view.window_label,"label":label}))
                    .collect(),
            ))),
            "plugin:webview|create_webview_window" => {
                let config = parse_window_options(
                    submission.args.get("options").unwrap_or(&submission.args),
                    "create_webview_window",
                )?;
                self.create_window(target, config, true, true)?;
                Ok(InvokeResult::Json(Value::Null))
            }
            "plugin:window|create" => {
                let config = parse_window_options(
                    submission.args.get("options").unwrap_or(&submission.args),
                    "create",
                )?;
                self.create_window(target, config, true, false)?;
                Ok(InvokeResult::Json(Value::Null))
            }
            "plugin:webview|create_webview" => {
                self.create_child_webview(submission)?;
                Ok(InvokeResult::Json(Value::Null))
            }
            _ if command.starts_with("plugin:webview|")
                || command == "webview.navigate"
                || command == "webview.postMessage" =>
            {
                self.invoke_webview(command, submission)
                    .map(InvokeResult::Json)
            }
            _ => self
                .invoke_window(command, submission)
                .map(InvokeResult::Json),
        }
    }

    fn create_child_webview(&mut self, submission: &Submission) -> Result<(), String> {
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

    fn invoke_window(&mut self, command: &str, submission: &Submission) -> Result<Value, String> {
        let label = submission
            .args
            .get("label")
            .and_then(Value::as_str)
            .or(submission.window_label.as_deref())
            .or_else(|| {
                self.webviews
                    .get(&submission.webview_label)
                    .map(|v| v.window_label.as_str())
            })
            .ok_or_else(|| "window command has no target label".to_string())?
            .to_owned();
        if command == "window.close" || command == "plugin:window|close" {
            if !self.windows.contains_key(&label) {
                return Err("window not found".to_string());
            }
            let prevent = self.shared.callback(CALLBACK_EVENT, 0, &json!({"eventName":"tauri://close-requested","label":label,"payload":null,"target":{"kind":"Window","label":label}}), &[]) == 1;
            if !prevent {
                self.remove_window(&label)?;
            }
            return Ok(Value::Null);
        }
        if command == "plugin:window|destroy" {
            self.remove_window(&label)?;
            return Ok(Value::Null);
        }
        if command == "plugin:window|set_icon" {
            let value = submission
                .args
                .get("value")
                .or_else(|| submission.args.get("icon"));
            let icon = self.resources.window_icon(value)?;
            self.windows
                .get(&label)
                .ok_or_else(|| "window not found".to_string())?
                .window
                .set_window_icon(icon);
            return Ok(Value::Null);
        }
        if command == "plugin:window|set_overlay_icon" {
            let icon = self.resources.window_icon(
                submission
                    .args
                    .get("value")
                    .or_else(|| submission.args.get("icon")),
            )?;
            set_overlay_icon(
                &self
                    .windows
                    .get(&label)
                    .ok_or_else(|| "window not found".to_string())?
                    .window,
                icon.as_ref(),
            )?;
            return Ok(Value::Null);
        }
        let state = self
            .windows
            .get_mut(&label)
            .ok_or_else(|| "window not found".to_string())?;
        if matches!(command, "window.maximize" | "plugin:window|maximize" |
            "window.minimize" | "plugin:window|minimize" | "plugin:window|toggle_maximize" |
            "plugin:window|internal_toggle_maximize" | "plugin:window|fullscreen" | "plugin:window|set_fullscreen") {
            state.capture_normal_bounds();
        }
        let window = &state.window;
        match command {
            "window.setTitle" => {
                window.set_title(required_string(&submission.args, "title")?);
                Ok(Value::Null)
            }
            "plugin:window|set_title" => {
                window.set_title(required_string(&submission.args, "value")?);
                Ok(Value::Null)
            }
            "window.show" | "plugin:window|show" => {
                window.set_visible(true);
                Ok(Value::Null)
            }
            "window.hide" | "plugin:window|hide" => {
                window.set_visible(false);
                Ok(Value::Null)
            }
            "window.minimize" | "plugin:window|minimize" => {
                window.set_minimized(true);
                Ok(Value::Null)
            }
            "window.maximize" | "plugin:window|maximize" => {
                window.set_maximized(true);
                Ok(Value::Null)
            }
            "plugin:window|unmaximize" => {
                window.set_maximized(false);
                Ok(Value::Null)
            }
            "plugin:window|unminimize" => {
                window.set_minimized(false);
                Ok(Value::Null)
            }
            "plugin:window|toggle_maximize" => {
                window.set_maximized(!window.is_maximized());
                Ok(Value::Null)
            }
            "plugin:window|internal_toggle_maximize" => {
                if window.is_resizable() {
                    if window.is_maximized() {
                        window.set_maximized(false);
                    } else if window.is_maximizable() {
                        window.set_maximized(true);
                    }
                }
                Ok(Value::Null)
            }
            "plugin:window|is_maximized" => Ok(json!(window.is_maximized())),
            "plugin:window|is_minimized" => Ok(json!(window.is_minimized())),
            "plugin:window|set_fullscreen" => {
                window.set_fullscreen(
                    bool_arg(&submission.args, "value")?.then_some(Fullscreen::Borderless(None)),
                );
                Ok(Value::Null)
            }
            "plugin:window|fullscreen" => {
                window.set_fullscreen(Some(Fullscreen::Borderless(None)));
                Ok(Value::Null)
            }
            "plugin:window|unfullscreen" => {
                window.set_fullscreen(None);
                Ok(Value::Null)
            }
            "plugin:window|is_fullscreen" => Ok(json!(window.fullscreen().is_some())),
            "plugin:window|set_focus" => {
                window.set_focus();
                Ok(Value::Null)
            }
            "plugin:window|is_focused" => Ok(json!(window.is_focused())),
            "plugin:window|is_visible" => Ok(json!(window.is_visible())),
            "plugin:window|title" => Ok(json!(window.title())),
            "plugin:window|scale_factor" => Ok(json!(window.scale_factor())),
            "plugin:window|is_resizable" => Ok(json!(window.is_resizable())),
            "plugin:window|is_maximizable" => Ok(json!(window.is_maximizable())),
            "plugin:window|is_minimizable" => Ok(json!(window.is_minimizable())),
            "plugin:window|is_closable" => Ok(json!(window.is_closable())),
            "plugin:window|is_enabled" => Ok(json!(state.enabled)),
            "plugin:window|is_always_on_top" => Ok(json!(window.is_always_on_top())),
            "plugin:window|set_resizable" => {
                window.set_resizable(bool_arg(&submission.args, "value")?);
                Ok(Value::Null)
            }
            "plugin:window|set_enabled" => {
                let value = bool_arg(&submission.args, "value")?;
                set_window_enabled(window, value);
                state.enabled = value;
                Ok(Value::Null)
            }
            "plugin:window|set_maximizable" => {
                window.set_maximizable(bool_arg(&submission.args, "value")?);
                Ok(Value::Null)
            }
            "plugin:window|set_minimizable" => {
                window.set_minimizable(bool_arg(&submission.args, "value")?);
                Ok(Value::Null)
            }
            "plugin:window|set_closable" => {
                window.set_closable(bool_arg(&submission.args, "value")?);
                Ok(Value::Null)
            }
            "plugin:window|is_decorated" => Ok(json!(window.is_decorated())),
            "plugin:window|set_decorations" => {
                window.set_decorations(bool_arg(&submission.args, "value")?);
                Ok(Value::Null)
            }
            "plugin:window|set_always_on_top" => {
                window.set_always_on_top(bool_arg(&submission.args, "value")?);
                Ok(Value::Null)
            }
            "plugin:window|set_always_on_bottom" => {
                window.set_always_on_bottom(bool_arg(&submission.args, "value")?);
                Ok(Value::Null)
            }
            "plugin:window|set_content_protected" => {
                window.set_content_protection(bool_arg(&submission.args, "value")?);
                Ok(Value::Null)
            }
            "plugin:window|set_visible_on_all_workspaces" => {
                window.set_visible_on_all_workspaces(bool_arg(&submission.args, "value")?);
                Ok(Value::Null)
            }
            "plugin:window|center" => {
                center_window(window);
                Ok(Value::Null)
            }
            "plugin:window|request_user_attention" => {
                window.request_user_attention(user_attention(submission.args.get("value"))?);
                Ok(Value::Null)
            }
            "plugin:window|set_size" => {
                let (w, h, logical) = size_value(
                    submission
                        .args
                        .get("value")
                        .ok_or("set_size requires value")?,
                )
                .map_err(|error| invalid_argument("set_size", "value", error))?;
                if logical {
                    window.set_inner_size(LogicalSize::new(w, h));
                } else {
                    window.set_inner_size(tao::dpi::PhysicalSize::new(w as u32, h as u32));
                }
                Ok(Value::Null)
            }
            "plugin:window|set_min_size" => {
                window.set_min_inner_size(optional_size(submission.args.get("value"))?);
                Ok(Value::Null)
            }
            "plugin:window|set_max_size" => {
                window.set_max_inner_size(optional_size(submission.args.get("value"))?);
                Ok(Value::Null)
            }
            "plugin:window|set_size_constraints" => {
                window.set_inner_size_constraints(size_constraints(
                    submission
                        .args
                        .get("value")
                        .ok_or("set_size_constraints requires value")?,
                )?);
                Ok(Value::Null)
            }
            "plugin:window|set_position" => {
                let (x, y, logical) = position_value(
                    submission
                        .args
                        .get("value")
                        .ok_or("set_position requires value")?,
                )
                .map_err(|error| invalid_argument("set_position", "value", error))?;
                if logical {
                    window.set_outer_position(LogicalPosition::new(x, y));
                } else {
                    window.set_outer_position(tao::dpi::PhysicalPosition::new(x as i32, y as i32));
                }
                Ok(Value::Null)
            }
            "plugin:window|inner_position" => {
                let p = window.inner_position().map_err(|e| e.to_string())?;
                Ok(json!({"x":p.x,"y":p.y}))
            }
            "plugin:window|outer_position" => {
                let p = window.outer_position().map_err(|e| e.to_string())?;
                Ok(json!({"x":p.x,"y":p.y}))
            }
            "plugin:window|inner_size" => {
                let s = window.inner_size();
                Ok(json!({"width":s.width,"height":s.height}))
            }
            "plugin:window|outer_size" => {
                let s = window.outer_size();
                Ok(json!({"width":s.width,"height":s.height}))
            }
            "plugin:window|current_monitor" => Ok(monitor_json(window.current_monitor())),
            "plugin:window|primary_monitor" => Ok(monitor_json(window.primary_monitor())),
            "plugin:window|monitor_from_point" => Ok(monitor_json(window.monitor_from_point(
                f64_arg(&submission.args, "x")?,
                f64_arg(&submission.args, "y")?,
            ))),
            "plugin:window|available_monitors" => Ok(Value::Array(
                window.available_monitors().map(monitor_value).collect(),
            )),
            "plugin:window|cursor_position" => {
                let p = window.cursor_position().map_err(|e| e.to_string())?;
                Ok(json!({"x":p.x,"y":p.y}))
            }
            "plugin:window|theme" => Ok(json!(theme_name(window.theme()))),
            "plugin:window|set_theme" => {
                window.set_theme(parse_theme(submission.args.get("value"))?);
                Ok(Value::Null)
            }
            "plugin:window|set_focusable" => {
                window.set_focusable(bool_arg(&submission.args, "value")?);
                Ok(Value::Null)
            }
            "plugin:window|set_skip_taskbar" => {
                set_skip_taskbar(window, bool_arg(&submission.args, "value")?)?;
                Ok(Value::Null)
            }
            "plugin:window|set_cursor_grab" => {
                window
                    .set_cursor_grab(bool_arg(&submission.args, "value")?)
                    .map_err(|e| e.to_string())?;
                Ok(Value::Null)
            }
            "plugin:window|set_cursor_visible" => {
                window.set_cursor_visible(bool_arg(&submission.args, "value")?);
                Ok(Value::Null)
            }
            "plugin:window|set_cursor_icon" => {
                window.set_cursor_icon(cursor_icon(required_string(&submission.args, "value")?)?);
                Ok(Value::Null)
            }
            "plugin:window|set_cursor_position" => {
                let (x, y, logical) = position_value(
                    submission
                        .args
                        .get("value")
                        .ok_or("set_cursor_position requires value")?,
                )
                .map_err(|error| invalid_argument("set_cursor_position", "value", error))?;
                let position: Position = if logical {
                    LogicalPosition::new(x, y).into()
                } else {
                    tao::dpi::PhysicalPosition::new(x as i32, y as i32).into()
                };
                window
                    .set_cursor_position(position)
                    .map_err(|e| e.to_string())?;
                Ok(Value::Null)
            }
            "plugin:window|set_ignore_cursor_events" => {
                window
                    .set_ignore_cursor_events(bool_arg(&submission.args, "value")?)
                    .map_err(|e| e.to_string())?;
                Ok(Value::Null)
            }
            "plugin:window|start_dragging" => {
                window.drag_window().map_err(|e| e.to_string())?;
                Ok(Value::Null)
            }
            "plugin:window|start_resize_dragging" => {
                window
                    .drag_resize_window(resize_direction(required_string(
                        &submission.args,
                        "value",
                    )?)?)
                    .map_err(|e| e.to_string())?;
                Ok(Value::Null)
            }
            "plugin:window|set_background_color" => {
                window.set_background_color(
                    optional_color_arg(&submission.args, "value").map_err(|error| {
                        invalid_argument("set_background_color", "value", error)
                    })?,
                );
                Ok(Value::Null)
            }
            "plugin:window|set_progress_bar" => {
                window.set_progress_bar(
                    progress_state(
                        submission
                            .args
                            .get("value")
                            .ok_or("set_progress_bar requires value")?,
                    )
                    .map_err(|error| invalid_argument("set_progress_bar", "value", error))?,
                );
                Ok(Value::Null)
            }
            "plugin:window|set_badge_count" => {
                let count = submission
                    .args
                    .get("value")
                    .map(|value| serde_json::from_value::<Option<i64>>(value.clone()))
                    .transpose()
                    .map_err(|error| invalid_argument("set_badge_count", "value", error))?
                    .flatten();
                let desktop_filename = optional_string(&submission.args, "desktopFilename")?;
                set_badge_count(window, count, desktop_filename);
                Ok(Value::Null)
            }
            "plugin:window|set_badge_label" => {
                let value = optional_string(&submission.args, "value")?;
                #[cfg(target_os = "macos")]
                { use tao::platform::macos::WindowExtMacOS; window.set_badge_label(value.map(str::to_owned)); }
                #[cfg(not(target_os = "macos"))]
                let _ = value;
                Ok(Value::Null)
            }
            "plugin:window|set_shadow" => {
                set_shadow(window, bool_arg(&submission.args, "value")?)?;
                Ok(Value::Null)
            }
            "plugin:window|set_effects" => {
                set_window_effects(window, submission.args.get("value"))?;
                Ok(Value::Null)
            }
            "plugin:window|set_simple_fullscreen" => {
                #[cfg(target_os = "macos")]
                { use tao::platform::macos::WindowExtMacOS; window.set_simple_fullscreen(bool_arg(&submission.args, "value")?); }
                #[cfg(not(target_os = "macos"))]
                window.set_fullscreen(
                    bool_arg(&submission.args, "value")?.then_some(Fullscreen::Borderless(None)),
                );
                Ok(Value::Null)
            }
            "plugin:window|set_title_bar_style" => {
                #[cfg(target_os = "macos")]
                crate::macos::set_title_bar_style(window, submission.args.get("value").ok_or("missing title bar style")?)?;
                Ok(Value::Null)
            },
            _ => Err(format!(
                "native window command '{command}' is not implemented"
            )),
        }
    }

    fn invoke_webview(&mut self, command: &str, submission: &Submission) -> Result<Value, String> {
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

    fn eval(&self, label: &str, document_id: u64, script: &str) -> Result<(), String> {
        let view = self.webviews.get(label);
        let current = view.map(|view| view.document_id.load(Ordering::Acquire));
        if stale_eval_document(current, document_id) {
            return Ok(());
        }
        let view = view.ok_or_else(|| "webview not found".to_string())?;
        view.native
            .evaluate_script(script)
            .map_err(|e| format!("script evaluation failed for '{label}': {e}"))
    }

    fn remove_webview(&mut self, label: &str) -> Result<(), String> {
        if let Some(view) = self.webviews.remove(label) {
            let id = view.document_id.load(Ordering::Acquire);
            let window_label = view.window_label.clone();
            let context_key = view.context_key.clone();
            self.shared.callback(
                CALLBACK_DOCUMENT,
                0,
                &json!({"webviewLabel":label,"documentId":id,"closed":true}),
                &[],
            );
            if let Some(window) = self.windows.get_mut(&window_label) {
                window.webviews.retain(|value| value != label);
            }
            // The browser object must be destroyed before its Wry context.
            drop(view);
            self.shared.remove_webview_origin(label);
            let remove_context = if let Some(context) = self.web_contexts.get_mut(&context_key) {
                context.webviews.remove(label);
                context.webviews.is_empty()
            } else {
                false
            };
            if remove_context {
                self.web_contexts.remove(&context_key);
            }
            Ok(())
        } else {
            Err("webview not found".to_string())
        }
    }

    fn remove_window(&mut self, label: &str) -> Result<(), String> {
        if let Some(mut window) = self.windows.remove(label) {
            self.event("netwiv://window-state", Some(label), window.persistence_snapshot(), "Window");
            let detached = window.menu.as_ref().or(self.app_menu.as_ref())
                .map(|menu| detach_menu(menu, &window.window)).transpose();
            self.sync_menu_accelerators();
            self.window_ids.remove(&window.window.id());
            for webview in window.webviews {
                self.remove_webview(&webview)?;
            }
            self.event("tauri://destroyed", Some(label), Value::Null, "Window");
            detached?;
            #[cfg(target_os = "macos")]
            self.sync_macos_menu();
            Ok(())
        } else {
            Err("window not found".to_string())
        }
    }

    fn shutdown(&mut self, control_flow: &mut ControlFlow) {
        let labels: Vec<_> = self.windows.keys().cloned().collect();
        for label in labels {
            let _ = self.remove_window(&label);
        }
        *control_flow = ControlFlow::Exit;
    }

    fn close_all_documents(&mut self) {
        let labels: Vec<_> = self.webviews.keys().cloned().collect();
        for label in labels {
            let _ = self.remove_webview(&label);
        }
    }

    fn resize_auto_webviews(
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

    fn event(&self, name: &str, label: Option<&str>, payload: Value, kind: &str) {
        self.shared.callback(CALLBACK_EVENT, 0, &json!({"eventName":name,"label":label,"payload":payload,"target":{"kind":kind,"label":label}}), &[]);
    }

    fn drain_native_events(&mut self) {
        self.global_shortcuts.drain(|payload| {
            self.shared.callback(CALLBACK_EVENT, 0,
                &json!({"eventName":"netwiv://global-shortcut","label":null,"payload":payload}), &[]);
        });
        while let Ok(event) = muda::MenuEvent::receiver().try_recv() {
            if let Some((target, id)) = self.resources.menu_event(event.id().0.as_str()) {
                if let Some(target) = target {
                    self.send_channel(target, json!(id));
                }
                self.event("menu-action", Some(&id), json!({"id":id}), "Any");
            }
        }
        while let Ok(event) = tray_icon::TrayIconEvent::receiver().try_recv() {
            let id = event.id().0.clone();
            let payload = match crate::tray_payload::serialize(&event) {
                Ok(payload) => payload,
                Err(error) => {
                    eprintln!("NetWiv tray event: {error}");
                    continue;
                }
            };
            if let Some((target, _)) = self.resources.channel_for_tray_event(&id) {
                self.send_channel(target, payload.clone());
            }
            self.event("tray-action", Some(&id), payload, "Any");
        }
    }

    fn send_channel(&self, target: ChannelTarget, message: Value) {
        let script = format!(
            "window.__TAURI_INTERNALS__.runCallback({},{{message:{},index:{}}});",
            target.channel_id, message, target.index
        );
        let _ = self.eval(&target.webview_label, target.document_id, &script);
    }

    fn send_channel_end(&self, target: ChannelTarget) {
        let script = format!(
            "window.__TAURI_INTERNALS__.runCallback({},{{end:true,index:{}}});",
            target.channel_id, target.index
        );
        let _ = self.eval(&target.webview_label, target.document_id, &script);
    }

    fn set_window_menu(&mut self, args: &Value, submission: &Submission) -> Result<Value, String> {
        let label = args
            .get("window")
            .and_then(Value::as_str)
            .or(submission.window_label.as_deref())
            .or_else(|| {
                self.webviews
                    .get(&submission.webview_label)
                    .map(|v| v.window_label.as_str())
            })
            .ok_or("set_as_window_menu has no window")?;
        let Some(state) = self.windows.get_mut(label) else {
            return Ok(Value::Null);
        };
        let menu = self
            .resources
            .root_menu_clone(crate::model::rid_arg(args)?)?;
        let previous = state.menu.clone().or_else(|| self.app_menu.clone());
        replace_attached_menu(previous.as_ref(), &menu, &state.window)?;
        state.menu = Some(menu);
        self.sync_menu_accelerators();
        previous
            .map(|menu| self.resources.add_root_menu_alias(menu))
            .transpose()
            .map(Option::unwrap_or_default)
    }
    fn set_app_menu(&mut self, args: &Value) -> Result<Value, String> {
        let menu = self
            .resources
            .root_menu_clone(crate::model::rid_arg(args)?)?;
        for window in self.windows.values() {
            if window.menu.is_none() {
                replace_attached_menu(self.app_menu.as_ref(), &menu, &window.window)?;
            }
        }
        let previous = self.app_menu.replace(menu);
        #[cfg(target_os = "macos")]
        self.sync_macos_menu();
        self.sync_menu_accelerators();
        previous
            .map(|menu| self.resources.add_root_menu_alias(menu))
            .transpose()
            .map(Option::unwrap_or_default)
    }
    #[cfg(target_os = "macos")]
    fn sync_macos_menu(&self) {
        if let Some(menu) = self.windows.values().find(|state| state.window.is_focused())
            .and_then(|state| state.menu.as_ref()).or(self.app_menu.as_ref()) {
            menu.init_for_nsapp();
        }
    }

    fn sync_menu_accelerators(&self) {
        #[cfg(target_os = "windows")]
        self.menu_accelerators.replace(self.app_menu.iter().chain(self.windows.values().filter_map(|window| window.menu.as_ref())));
    }
    fn popup_menu(&self, args: &Value, submission: &Submission) -> Result<Value, String> {
        let label = args
            .get("window")
            .and_then(Value::as_str)
            .or(submission.window_label.as_deref())
            .or_else(|| {
                self.webviews
                    .get(&submission.webview_label)
                    .map(|v| v.window_label.as_str())
            })
            .ok_or("menu popup has no window")?;
        let Some(window) = self.windows.get(label).map(|state| &state.window) else {
            return Ok(Value::Null);
        };
        let menu = self.resources.popup_menu_clone(args)?;
        let at: Option<muda::dpi::Position> = args
            .get("at")
            .map(|v| {
                position_value(v).map(|(x, y, logical)| {
                    if logical {
                        muda::dpi::LogicalPosition::new(x, y).into()
                    } else {
                        muda::dpi::PhysicalPosition::new(x as i32, y as i32).into()
                    }
                })
            })
            .transpose()?;
        #[cfg(target_os = "windows")]
        {
            use tao::platform::windows::WindowExtWindows;
            unsafe { menu.show_context_menu_for_hwnd(window.hwnd(), at) };
        }
        #[cfg(target_os = "linux")]
        {
            use gtk::prelude::Cast;
            use tao::platform::unix::WindowExtUnix;
            menu.show_context_menu_for_gtk_window(
                window.gtk_window().upcast_ref::<gtk::Window>(),
                at,
            );
        }
        #[cfg(target_os = "macos")]
        {
            use tao::platform::macos::WindowExtMacOS;
            // SAFETY: this view belongs to the live UI-thread window.
            unsafe { menu.show_context_menu_for_nsview(window.ns_view(), at); }
        }
        Ok(Value::Null)
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

fn stale_eval_document(current: Option<u64>, requested: u64) -> bool {
    requested != 0 && current != Some(requested)
}

enum InvokeResult {
    Json(Value),
    #[allow(dead_code)]
    Binary(Vec<u8>),
}

fn bridge_script(
    window: &str,
    webview: &str,
    use_https_scheme: bool,
    protocol_ipc: bool,
) -> String {
    let os = if cfg!(target_os = "windows") {
        "windows"
    } else if cfg!(target_os = "macos") {
        "macos"
    } else {
        "linux"
    };
    format!(
        "(()=>{{const bridge=Object.freeze({{os:{},windowLabel:{},webviewLabel:{},protocolScheme:{},protocolIpc:{protocol_ipc},postMessage(message){{window.ipc.postMessage(typeof message==='string'?message:JSON.stringify(message));}}}});Object.defineProperty(globalThis,'__NETWIV_IPC__',{{value:bridge,configurable:false,writable:false}});}})();",
        serde_json::to_string(os).unwrap(),
        serde_json::to_string(window).unwrap(),
        serde_json::to_string(webview).unwrap(),
        serde_json::to_string(if use_https_scheme { "https" } else { "http" }).unwrap()
    )
}

fn webview_origin(initial_url: &str, use_https_scheme: bool) -> String {
    let Ok(url) = url::Url::parse(initial_url) else {
        return "null".to_string();
    };
    if url.scheme() == "data" {
        "null".to_string()
    } else if cfg!(target_os = "windows") && url.scheme() != "http" && url.scheme() != "https" {
        let scheme = if use_https_scheme { "https" } else { "http" };
        format!("{scheme}://{}.localhost", url.scheme())
    } else if let Some(host) = url.host() {
        format!(
            "{}://{}{}",
            url.scheme(),
            host,
            url.port()
                .map(|port| format!(":{port}"))
                .unwrap_or_default()
        )
    } else {
        "null".to_string()
    }
}

fn parse_window_options(options: &Value, command: &str) -> Result<WindowConfig, String> {
    let parsed = parse_tauri_window_config(options, command)?;
    validate_runtime_label(&parsed.label, "window")?;
    if parsed.width <= 0.0 || parsed.height <= 0.0 {
        return Err("window width and height must be positive".into());
    }
    let extra_config = canonical_window_options(&parsed)?;
    Ok(WindowConfig {
        title: parsed.title,
        url: parsed.url.to_string(),
        label: parsed.label,
        width: parsed.width,
        height: parsed.height,
        extra_config,
        webviews: None,
    })
}

fn parse_webview_options(options: &Value, command: &str) -> Result<WebviewConfig, String> {
    let parsed = parse_tauri_window_config(options, command)?;
    validate_runtime_label(&parsed.label, "webview")?;
    if parsed.width <= 0.0 || parsed.height <= 0.0 {
        return Err("webview width and height must be positive".into());
    }
    let extra_config = canonical_window_options(&parsed)?;
    Ok(WebviewConfig {
        label: parsed.label,
        url: parsed.url.to_string(),
        x: parsed.x.unwrap_or(0.0),
        y: parsed.y.unwrap_or(0.0),
        width: parsed.width,
        height: parsed.height,
        extra_config,
    })
}

fn parse_tauri_window_config(options: &Value, command: &str) -> Result<TauriWindowConfig, String> {
    let mut config: TauriWindowConfig = serde_json::from_value(options.clone())
        .map_err(|error| format!("invalid args `options` for command `{command}`: {error}"))?;
    if options.get("title").is_none() {
        config.title = "NetWiv App".into();
    }
    Ok(config)
}

fn parse_child_webview_options(options: &Value, is_backend: bool) -> Result<WebviewConfig, String> {
    let mut options = options.clone();
    // auto_resize is a Rust builder option, absent from the guest WindowConfig.
    // Only trusted C# submissions may use the corresponding builder field.
    let auto_resize = if is_backend {
        options.as_object_mut().and_then(|fields| fields.remove("autoResize"))
    } else {
        None
    };
    let mut config = parse_webview_options(&options, "create_webview")?;
    let fields = config.extra_config.as_object_mut().expect("normalized window config");
    if let Some(value) = auto_resize {
        let value: Option<bool> = serde_json::from_value(value)
            .map_err(|error| invalid_argument("create_webview", "options", error))?;
        if let Some(value) = value { fields.insert("autoResize".into(), json!(value)); }
    }
    if !is_backend {
        // The pinned guest's WindowConfig visibility does not apply to its
        // WebviewAttributes. C# retains its explicit child Visible extension.
        fields.remove("visible");
    }
    Ok(config)
}

fn canonical_window_options(config: &TauriWindowConfig) -> Result<Value, String> {
    let mut value = serde_json::to_value(config).map_err(|error| error.to_string())?;
    // Serialization supplies pinned defaults and turns accepted aliases into
    // the camel-case keys used by the native adapter. Absent optional settings
    // must remain absent so creation does not execute their setter paths.
    value.as_object_mut().expect("WindowConfig serializes as an object")
        .retain(|_, value| !value.is_null());
    Ok(value)
}

fn invalid_argument(command: &str, argument: &str, error: impl std::fmt::Display) -> String {
    format!("invalid args `{argument}` for command `{command}`: {error}")
}

#[derive(Deserialize)]
enum IpcSize {
    Logical { width: f64, height: f64 },
    Physical { width: u32, height: u32 },
}

fn size_value(value: &Value) -> Result<(f64, f64, bool), String> {
    match serde_json::from_value::<IpcSize>(value.clone()).map_err(|error| error.to_string())? {
        IpcSize::Logical { width, height } => Ok((width, height, true)),
        IpcSize::Physical { width, height } => Ok((width.into(), height.into(), false)),
    }
}

#[derive(Deserialize)]
enum IpcPosition {
    Logical { x: f64, y: f64 },
    Physical { x: i32, y: i32 },
}

fn position_value(value: &Value) -> Result<(f64, f64, bool), String> {
    match serde_json::from_value::<IpcPosition>(value.clone()).map_err(|error| error.to_string())? {
        IpcPosition::Logical { x, y } => Ok((x, y, true)),
        IpcPosition::Physical { x, y } => Ok((x.into(), y.into(), false)),
    }
}
fn size_json(rect: Rect, scale_factor: f64) -> Value {
    let value = rect.size.to_physical::<u32>(scale_factor);
    json!({"width":value.width,"height":value.height})
}
fn position_json(rect: Rect, scale_factor: f64) -> Value {
    let value = rect.position.to_physical::<i32>(scale_factor);
    json!({"x":value.x,"y":value.y})
}
fn sorted<'a>(values: impl Iterator<Item = &'a String>) -> Vec<String> {
    let mut result: Vec<_> = values.cloned().collect();
    result.sort();
    result
}

#[cfg(target_os = "linux")]
fn platform_backend(target: &EventLoopWindowTarget<UserEvent>) -> &'static str {
    use tao::platform::unix::EventLoopWindowTargetExtUnix;
    if target.is_wayland() {
        "wayland"
    } else {
        "x11"
    }
}

#[cfg(target_os = "windows")]
fn platform_backend(_: &EventLoopWindowTarget<UserEvent>) -> &'static str {
    "windows"
}

#[cfg(target_os = "macos")]
fn platform_backend(_: &EventLoopWindowTarget<UserEvent>) -> &'static str { "macos" }

#[cfg(target_os = "macos")]
fn monitor_work_area(monitor: &MonitorHandle) -> (tao::dpi::PhysicalPosition<i32>, tao::dpi::PhysicalSize<u32>) {
    crate::macos::monitor_work_area(monitor)
}

fn set_window_enabled(window: &Window, enabled: bool) {
    #[cfg(target_os = "macos")]
    { window.set_ignore_cursor_events(!enabled).ok(); window.set_focusable(enabled); }
    #[cfg(target_os = "windows")]
    {
        use tao::platform::windows::WindowExtWindows;
        window.set_enable(enabled);
    }
    #[cfg(target_os = "linux")]
    {
        use gtk::prelude::*;
        use tao::platform::unix::WindowExtUnix;
        window.gtk_window().set_sensitive(enabled);
    }
}

fn center_window(window: &Window) {
    let Some(monitor) = window.current_monitor() else {
        return;
    };
    let (work_position, work_size) = monitor_work_area(&monitor);
    let size = window.outer_size();
    let x = (i64::from(work_size.width) - i64::from(size.width)) / 2 + i64::from(work_position.x);
    let y = (i64::from(work_size.height) - i64::from(size.height)) / 2 + i64::from(work_position.y);
    window.set_outer_position(tao::dpi::PhysicalPosition::new(
        x.clamp(i64::from(i32::MIN), i64::from(i32::MAX)) as i32,
        y.clamp(i64::from(i32::MIN), i64::from(i32::MAX)) as i32,
    ));
}

fn monitor_json(monitor: Option<MonitorHandle>) -> Value {
    monitor.map(monitor_value).unwrap_or(Value::Null)
}

fn monitor_value(monitor: MonitorHandle) -> Value {
    let position = monitor.position();
    let size = monitor.size();
    let (work_position, work_size) = monitor_work_area(&monitor);
    json!({
        "name":monitor.name(),
        "position":{"x":position.x,"y":position.y},
        "size":{"width":size.width,"height":size.height},
        "workArea":{"position":{"x":work_position.x,"y":work_position.y},"size":{"width":work_size.width,"height":work_size.height}},
        "scaleFactor":monitor.scale_factor()
    })
}

#[cfg(target_os = "windows")]
fn monitor_work_area(
    monitor: &MonitorHandle,
) -> (tao::dpi::PhysicalPosition<i32>, tao::dpi::PhysicalSize<u32>) {
    use tao::platform::windows::MonitorHandleExtWindows;
    use windows::Win32::Graphics::Gdi::{GetMonitorInfoW, HMONITOR, MONITORINFO};
    let mut info = MONITORINFO {
        cbSize: std::mem::size_of::<MONITORINFO>() as u32,
        ..Default::default()
    };
    // SAFETY: hmonitor is owned by tao and info has the size expected by GetMonitorInfoW.
    if unsafe { GetMonitorInfoW(HMONITOR(monitor.hmonitor() as _), &mut info) }.as_bool() {
        (
            tao::dpi::PhysicalPosition::new(info.rcWork.left, info.rcWork.top),
            tao::dpi::PhysicalSize::new(
                (info.rcWork.right - info.rcWork.left) as u32,
                (info.rcWork.bottom - info.rcWork.top) as u32,
            ),
        )
    } else {
        (monitor.position(), monitor.size())
    }
}

#[cfg(target_os = "linux")]
fn monitor_work_area(
    monitor: &MonitorHandle,
) -> (tao::dpi::PhysicalPosition<i32>, tao::dpi::PhysicalSize<u32>) {
    use gtk::prelude::MonitorExt;
    use tao::platform::unix::MonitorHandleExtUnix;
    let rect = monitor.gdk_monitor().workarea();
    let scale = monitor.scale_factor();
    (
        LogicalPosition::new(rect.x(), rect.y()).to_physical(scale),
        LogicalSize::new(rect.width() as u32, rect.height() as u32).to_physical(scale),
    )
}

fn user_attention(value: Option<&Value>) -> Result<Option<UserAttentionType>, String> {
    let Some(value) = value else { return Ok(None) };
    if value.is_null() {
        return Ok(None);
    }
    let kind = value
        .get("type")
        .and_then(Value::as_str)
        .ok_or("user attention value requires type")?;
    match kind.to_ascii_lowercase().as_str() {
        "critical" => Ok(Some(UserAttentionType::Critical)),
        "informational" => Ok(Some(UserAttentionType::Informational)),
        _ => Err(format!("unsupported user attention type '{kind}'")),
    }
}

fn optional_size(value: Option<&Value>) -> Result<Option<Size>, String> {
    let Some(value) = value else { return Ok(None) };
    if value.is_null() {
        return Ok(None);
    }
    let (width, height, logical) = size_value(value)?;
    Ok(Some(if logical {
        LogicalSize::new(width, height).into()
    } else {
        tao::dpi::PhysicalSize::new(width as u32, height as u32).into()
    }))
}

fn optional_string<'a>(value: &'a Value, name: &str) -> Result<Option<&'a str>, String> {
    match value.get(name) {
        None | Some(Value::Null) => Ok(None),
        Some(value) => value
            .as_str()
            .map(Some)
            .ok_or_else(|| format!("window option '{name}' must be a string or null")),
    }
}

fn optional_bool(value: &Value, name: &str) -> Result<Option<bool>, String> {
    match value.get(name) {
        None | Some(Value::Null) => Ok(None),
        Some(Value::Bool(value)) => Ok(Some(*value)),
        Some(_) => Err(format!("window option '{name}' must be a boolean or null")),
    }
}

fn prevent_overflow_margin(
    value: Option<&Value>,
) -> Result<Option<tao::dpi::PhysicalSize<u32>>, String> {
    match value {
        None | Some(Value::Null) | Some(Value::Bool(false)) => Ok(None),
        Some(Value::Bool(true)) => Ok(Some(tao::dpi::PhysicalSize::new(0, 0))),
        Some(Value::Object(value)) => {
            let dimension = |name: &str| {
                value
                    .get(name)
                    .and_then(Value::as_u64)
                    .and_then(|value| u32::try_from(value).ok())
                    .ok_or_else(|| {
                        format!("preventOverflow margin '{name}' must be an unsigned integer")
                    })
            };
            Ok(Some(tao::dpi::PhysicalSize::new(
                dimension("width")?,
                dimension("height")?,
            )))
        }
        Some(_) => Err("preventOverflow must be a boolean or physical size".into()),
    }
}

fn constrained_inner_size(
    inner: tao::dpi::PhysicalSize<u32>,
    outer: tao::dpi::PhysicalSize<u32>,
    work_area: tao::dpi::PhysicalSize<u32>,
    margin: tao::dpi::PhysicalSize<u32>,
) -> tao::dpi::PhysicalSize<u32> {
    let constraint = tao::dpi::PhysicalSize::new(
        work_area.width.saturating_sub(margin.width),
        work_area.height.saturating_sub(margin.height),
    );
    tao::dpi::PhysicalSize::new(
        inner
            .width
            .saturating_sub(outer.width.saturating_sub(constraint.width)),
        inner
            .height
            .saturating_sub(outer.height.saturating_sub(constraint.height)),
    )
}

fn apply_prevent_overflow(window: &Window, margin: tao::dpi::PhysicalSize<u32>) {
    let Some(monitor) = window.current_monitor() else {
        return;
    };
    let (_, work_area) = monitor_work_area(&monitor);
    let inner = window.inner_size();
    let constrained = constrained_inner_size(inner, window.outer_size(), work_area, margin);
    if constrained != inner {
        window.set_inner_size(constrained);
    }
}

fn resolve_webview_data_directory(
    local_data_directory: Option<&Path>,
    label: &str,
    value: Option<&Value>,
) -> Result<Option<PathBuf>, String> {
    let Some(value) = value else { return Ok(None) };
    if value.is_null() {
        return Ok(None);
    }
    let raw = PathBuf::from(
        value
            .as_str()
            .ok_or("webview option 'dataDirectory' must be a path string or null")?,
    );
    let safe = !raw
        .components()
        .any(|component| matches!(component, Component::ParentDir));
    if safe
        && raw.is_relative()
        && let Some(root) = local_data_directory
            .map(Path::to_path_buf)
            .or_else(dirs::data_local_dir)
    {
        return Ok(Some(root.join(label).join(raw)));
    }
    // This preserves pinned `WebviewBuilder::from_config`: validation failure logs that
    // the option is ignored, but the original value remains in the cloned config and is
    // forwarded to the runtime unchanged.
    Ok(Some(raw))
}

fn parse_proxy_url(value: Option<&Value>) -> Result<Option<ProxyConfig>, String> {
    let Some(value) = value else { return Ok(None) };
    if value.is_null() {
        return Ok(None);
    }
    let raw = value
        .as_str()
        .ok_or("webview option 'proxyUrl' must be a URL string or null")?;
    let url = url::Url::parse(raw).map_err(|error| format!("invalid proxy URL: {error}"))?;
    let endpoint = ProxyEndpoint {
        host: url.host().map(|host| host.to_string()).unwrap_or_default(),
        port: url.port().map(|port| port.to_string()).unwrap_or_default(),
    };
    match url.scheme() {
        "http" => Ok(Some(ProxyConfig::Http(endpoint))),
        "socks5" => Ok(Some(ProxyConfig::Socks5(endpoint))),
        _ => Err("proxy URL must use http:// or socks5://".into()),
    }
}

fn size_constraints(value: &Value) -> Result<WindowSizeConstraints, String> {
    Ok(WindowSizeConstraints {
        min_width: pixel_unit(value.get("minWidth"))?,
        min_height: pixel_unit(value.get("minHeight"))?,
        max_width: pixel_unit(value.get("maxWidth"))?,
        max_height: pixel_unit(value.get("maxHeight"))?,
    })
}

fn logical_window_constraints(value: &Value) -> Result<WindowSizeConstraints, String> {
    Ok(WindowSizeConstraints {
        min_width: logical_constraint(value.get("minWidth"), "minWidth")?,
        min_height: logical_constraint(value.get("minHeight"), "minHeight")?,
        max_width: logical_constraint(value.get("maxWidth"), "maxWidth")?,
        max_height: logical_constraint(value.get("maxHeight"), "maxHeight")?,
    })
}

fn logical_constraint(value: Option<&Value>, name: &str) -> Result<Option<PixelUnit>, String> {
    let Some(value) = value else { return Ok(None) };
    let value = value
        .as_f64()
        .filter(|value| value.is_finite() && *value > 0.0)
        .ok_or_else(|| format!("window option '{name}' must be a positive finite number"))?;
    Ok(Some(LogicalUnit::new(value).into()))
}

fn pixel_unit(value: Option<&Value>) -> Result<Option<PixelUnit>, String> {
    let Some(value) = value else { return Ok(None) };
    if value.is_null() {
        return Ok(None);
    }
    if let Some(value) = value.get("Logical").and_then(Value::as_f64) {
        return Ok(Some(LogicalUnit::new(value).into()));
    }
    if let Some(value) = value.get("Physical").and_then(Value::as_i64) {
        let value = i32::try_from(value).map_err(|_| "physical constraint is out of range")?;
        return Ok(Some(PhysicalUnit::new(value).into()));
    }
    Err("size constraint must be Logical, Physical, or null".into())
}

fn parse_theme(value: Option<&Value>) -> Result<Option<Theme>, String> {
    match value.and_then(Value::as_str) {
        None => Ok(None),
        Some("light") => Ok(Some(Theme::Light)),
        Some("dark") => Ok(Some(Theme::Dark)),
        Some(other) => Err(format!("unsupported theme '{other}'")),
    }
}

fn theme_name(theme: Theme) -> &'static str {
    match theme {
        Theme::Light => "light",
        Theme::Dark => "dark",
        _ => "light",
    }
}

fn cursor_icon(value: &str) -> Result<CursorIcon, String> {
    Ok(match value {
        "default" => CursorIcon::Default,
        "crosshair" => CursorIcon::Crosshair,
        "hand" | "pointer" => CursorIcon::Hand,
        "arrow" => CursorIcon::Arrow,
        "move" => CursorIcon::Move,
        "text" => CursorIcon::Text,
        "wait" => CursorIcon::Wait,
        "help" => CursorIcon::Help,
        "progress" => CursorIcon::Progress,
        "notAllowed" | "not-allowed" => CursorIcon::NotAllowed,
        "contextMenu" | "context-menu" => CursorIcon::ContextMenu,
        "cell" => CursorIcon::Cell,
        "verticalText" | "vertical-text" => CursorIcon::VerticalText,
        "alias" => CursorIcon::Alias,
        "copy" => CursorIcon::Copy,
        "noDrop" | "no-drop" => CursorIcon::NoDrop,
        "grab" => CursorIcon::Grab,
        "grabbing" => CursorIcon::Grabbing,
        "allScroll" | "all-scroll" => CursorIcon::AllScroll,
        "zoomIn" | "zoom-in" => CursorIcon::ZoomIn,
        "zoomOut" | "zoom-out" => CursorIcon::ZoomOut,
        "eResize" | "e-resize" => CursorIcon::EResize,
        "nResize" | "n-resize" => CursorIcon::NResize,
        "neResize" | "ne-resize" => CursorIcon::NeResize,
        "nwResize" | "nw-resize" => CursorIcon::NwResize,
        "sResize" | "s-resize" => CursorIcon::SResize,
        "seResize" | "se-resize" => CursorIcon::SeResize,
        "swResize" | "sw-resize" => CursorIcon::SwResize,
        "wResize" | "w-resize" => CursorIcon::WResize,
        "ewResize" | "ew-resize" => CursorIcon::EwResize,
        "nsResize" | "ns-resize" => CursorIcon::NsResize,
        "neswResize" | "nesw-resize" => CursorIcon::NeswResize,
        "nwseResize" | "nwse-resize" => CursorIcon::NwseResize,
        "colResize" | "col-resize" => CursorIcon::ColResize,
        "rowResize" | "row-resize" => CursorIcon::RowResize,
        other => return Err(format!("unsupported cursor icon '{other}'")),
    })
}

fn resize_direction(value: &str) -> Result<ResizeDirection, String> {
    Ok(match value.to_ascii_lowercase().as_str() {
        "east" => ResizeDirection::East,
        "north" => ResizeDirection::North,
        "northeast" => ResizeDirection::NorthEast,
        "northwest" => ResizeDirection::NorthWest,
        "south" => ResizeDirection::South,
        "southeast" => ResizeDirection::SouthEast,
        "southwest" => ResizeDirection::SouthWest,
        "west" => ResizeDirection::West,
        _ => return Err(format!("unsupported resize direction '{value}'")),
    })
}

fn color_arg(value: &Value, name: &str) -> Result<(u8, u8, u8, u8), String> {
    let value = value
        .get(name)
        .ok_or_else(|| format!("command argument '{name}' is required"))?;
    parse_color(value)
}

fn parse_color(value: &Value) -> Result<(u8, u8, u8, u8), String> {
    serde_json::from_value::<TauriColor>(value.clone())
        .map(Into::into)
        .map_err(|error| error.to_string())
}
fn optional_color_arg(value: &Value, name: &str) -> Result<Option<(u8, u8, u8, u8)>, String> {
    match value.get(name) {
        None | Some(Value::Null) => Ok(None),
        Some(_) => color_arg(value, name).map(Some),
    }
}

#[derive(Deserialize)]
#[serde(rename_all = "lowercase")]
enum IpcProgressState {
    None,
    Normal,
    Indeterminate,
    Paused,
    Error,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct IpcProgressBarState {
    status: Option<IpcProgressState>,
    progress: Option<u64>,
    desktop_filename: Option<String>,
}

fn progress_state(value: &Value) -> Result<ProgressBarState, String> {
    let value: IpcProgressBarState =
        serde_json::from_value(value.clone()).map_err(|error| error.to_string())?;
    let state = value.status.map(|state| match state {
        IpcProgressState::None => ProgressState::None,
        IpcProgressState::Normal => ProgressState::Normal,
        IpcProgressState::Indeterminate => ProgressState::Indeterminate,
        IpcProgressState::Paused => ProgressState::Paused,
        IpcProgressState::Error => ProgressState::Error,
    });
    Ok(ProgressBarState {
        state,
        progress: value.progress,
        desktop_filename: value.desktop_filename,
    })
}
#[cfg(target_os = "macos")]
fn set_skip_taskbar(_: &Window, skip: bool) -> Result<(), String> {
    if skip { Err("Per-window skipTaskbar is unsupported on macOS; the Dock represents the application.".into()) } else { Ok(()) }
}

#[cfg(not(target_os = "macos"))]
fn set_skip_taskbar(window: &Window, skip: bool) -> Result<(), String> {
    #[cfg(target_os = "linux")]
    use tao::platform::unix::WindowExtUnix;
    #[cfg(target_os = "windows")]
    use tao::platform::windows::WindowExtWindows;
    window
        .set_skip_taskbar(skip)
        .map_err(|error| error.to_string())
}

fn set_shadow(window: &Window, enabled: bool) -> Result<(), String> {
    #[cfg(target_os = "macos")]
    { use tao::platform::macos::WindowExtMacOS; window.set_has_shadow(enabled); Ok(()) }
    #[cfg(target_os = "windows")]
    {
        use tao::platform::windows::WindowExtWindows;
        window.set_undecorated_shadow(enabled);
        Ok(())
    }
    #[cfg(target_os = "linux")]
    {
        let _ = (window, enabled);
        Ok(())
    }
}

fn decode_window_effects(value: Option<&Value>) -> Result<Option<tauri_utils::config::WindowEffectsConfig>, String> {
    // Decode before selecting the platform path: even Linux's no-op validates
    // the complete public argument, including Apple-only fields and unknown keys.
    serde_json::from_value(value.cloned().unwrap_or(Value::Null))
        .map_err(|error| invalid_argument("set_effects", "value", error))
}

fn set_window_effects(window: &Window, value: Option<&Value>) -> Result<(), String> {
    let config = decode_window_effects(value)?;
    #[cfg(target_os = "macos")]
    crate::macos::set_effects(window, config)?;
    #[cfg(target_os = "windows")]
    {
        use tauri_utils::WindowEffect;
        let Some(config) = config else {
            let _ = window_vibrancy::clear_blur(window);
            let _ = window_vibrancy::clear_acrylic(window);
            let _ = window_vibrancy::clear_mica(window);
            return Ok(());
        };
        let color = config.color.map(Into::into);
        let effect = config.effects.into_iter().find(|effect| matches!(effect,
            WindowEffect::Blur | WindowEffect::Acrylic | WindowEffect::Mica |
            WindowEffect::MicaDark | WindowEffect::MicaLight | WindowEffect::Tabbed |
            WindowEffect::TabbedDark | WindowEffect::TabbedLight));
        // Pinned Tauri deliberately ignores OS effect-application failures (for
        // example an unavailable Windows effect); retain that observable contract.
        match effect {
            Some(WindowEffect::Blur) => { let _ = window_vibrancy::apply_blur(window, color); }
            Some(WindowEffect::Acrylic) => { let _ = window_vibrancy::apply_acrylic(window, color); }
            Some(WindowEffect::Mica) => { let _ = window_vibrancy::apply_mica(window, None); }
            Some(WindowEffect::MicaDark) => { let _ = window_vibrancy::apply_mica(window, Some(true)); }
            Some(WindowEffect::MicaLight) => { let _ = window_vibrancy::apply_mica(window, Some(false)); }
            Some(WindowEffect::Tabbed) => { let _ = window_vibrancy::apply_tabbed(window, None); }
            Some(WindowEffect::TabbedDark) => { let _ = window_vibrancy::apply_tabbed(window, Some(true)); }
            Some(WindowEffect::TabbedLight) => { let _ = window_vibrancy::apply_tabbed(window, Some(false)); }
            _ => {}
        }
    }
    #[cfg(target_os = "linux")]
    let _ = (window, config);
    Ok(())
}

fn set_overlay_icon(window: &Window, icon: Option<&tao::window::Icon>) -> Result<(), String> {
    #[cfg(target_os = "windows")]
    {
        use tao::platform::windows::WindowExtWindows;
        window.set_overlay_icon(icon);
        Ok(())
    }
    #[cfg(any(target_os = "linux", target_os = "macos"))]
    {
        let _ = (window, icon);
        Err("window overlay icons are only supported on Windows".into())
    }
}

fn set_badge_count(window: &Window, count: Option<i64>, desktop_filename: Option<&str>) {
    #[cfg(target_os = "macos")]
    {
        use tao::platform::macos::WindowExtMacOS;
        let _ = desktop_filename;
        window.set_badge_label(count.map(|value| value.to_string()));
    }
    #[cfg(target_os = "linux")]
    {
        use tao::platform::unix::WindowExtUnix;
        window.set_badge_count(count, desktop_filename.map(str::to_owned));
    }
    #[cfg(target_os = "windows")]
    let _ = (window, count, desktop_filename);
}

fn replace_attached_menu(previous: Option<&muda::Menu>, next: &muda::Menu, window: &Window) -> Result<(), String> {
    #[cfg(target_os = "macos")]
    if !window.is_focused() { return Ok(()); }
    if let Some(previous) = previous {
        detach_menu(previous, window)?;
    }
    attach_menu(next, window)
}

fn detach_menu(menu: &muda::Menu, window: &Window) -> Result<(), String> {
    #[cfg(target_os = "macos")]
    { let _ = window; menu.remove_for_nsapp(); Ok(()) }
    #[cfg(target_os = "windows")]
    {
        use tao::platform::windows::WindowExtWindows;
        // SAFETY: the runtime owns this live window and attached menu on its UI thread.
        unsafe { menu.remove_for_hwnd(window.hwnd()) }.map_err(|error| error.to_string())
    }
    #[cfg(target_os = "linux")]
    {
        use tao::platform::unix::WindowExtUnix;
        menu.remove_for_gtk_window(window.gtk_window()).map_err(|error| error.to_string())
    }
}

fn attach_menu(menu: &muda::Menu, window: &Window) -> Result<(), String> {
    #[cfg(target_os = "macos")]
    { let _ = window; menu.init_for_nsapp(); Ok(()) }
    #[cfg(target_os = "windows")]
    {
        use tao::platform::windows::WindowExtWindows;
        unsafe { menu.init_for_hwnd(window.hwnd()) }.map_err(|e| e.to_string())
    }
    #[cfg(target_os = "linux")]
    {
        use tao::platform::unix::WindowExtUnix;
        menu.init_for_gtk_window(window.gtk_window(), window.default_vbox())
            .map_err(|e| e.to_string())
    }
}

#[cfg(test)]
mod tests {
    use std::path::{Path, PathBuf};

    use crate::model::RunConfig;
    use serde_json::json;
    use tao::dpi::PhysicalSize;
    use wry::ProxyConfig;

    use super::{
        build_asset_response, constrained_inner_size, invalid_argument, parse_color,
        parse_proxy_url, parse_webview_options, parse_window_options, position_value,
        prevent_overflow_margin, resolve_webview_data_directory, size_value, stale_eval_document,
        webview_origin,
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
            let error = super::decode_window_effects(Some(&value)).unwrap_err();
            assert!(error.starts_with("invalid args `value` for command `set_effects`:"), "{error}");
            // Creation uses the same pinned config decoder, before allocating a window.
            assert!(super::parse_window_options(&json!({"label":"effects-invalid", "windowEffects": value}), "create").is_err());
        }
    }

    #[test]
    fn window_effects_accept_clear_and_preserve_order_and_typed_fields() {
        assert!(super::decode_window_effects(None).unwrap().is_none());
        assert!(super::decode_window_effects(Some(&serde_json::Value::Null)).unwrap().is_none());
        let effects = super::decode_window_effects(Some(&json!({
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
        super::replace_attached_menu(None, &first, &window).unwrap();
        super::replace_attached_menu(Some(&first), &second, &window).unwrap();
        // Reusing the same menu must remove the prior native attachment as well.
        super::replace_attached_menu(Some(&second), &second, &window).unwrap();
        #[cfg(target_os = "linux")]
        {
            use gtk::prelude::*;
            use tao::platform::unix::WindowExtUnix;
            let bars = || window.default_vbox().unwrap().children().iter().filter(|widget| widget.is::<gtk::MenuBar>()).count();
            assert_eq!(bars(), 1, "menu replacement accumulated GTK menubars");
            super::detach_menu(&second, &window).unwrap();
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
            super::detach_menu(&second, &window).unwrap();
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
        let origin = webview_origin(&super::resolve_url("index.html", true), true);
        assert_eq!(
            origin,
            if cfg!(target_os = "windows") {
                "https://tauri.localhost"
            } else {
                "tauri://localhost"
            }
        );
        assert!(
            super::bridge_script("main", "secondary", true, true)
                .contains("protocolScheme:\"https\"")
        );
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
        assert_eq!(
            resolve_webview_data_directory(Some(root), "main", Some(&json!("profile"))).unwrap(),
            Some(root.join("main").join("profile"))
        );
        assert_eq!(
            resolve_webview_data_directory(Some(root), "main", Some(&json!("../shared"))).unwrap(),
            Some(PathBuf::from("../shared"))
        );
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
        let backend = super::parse_child_webview_options(&options, true).unwrap();
        assert_eq!(backend.extra_config["autoResize"], true);
        assert_eq!(backend.extra_config["visible"], false);
        assert!(super::parse_child_webview_options(&options, false).unwrap_err().contains("unknown field `autoResize`"));
        assert!(super::parse_child_webview_options(&json!({"autoResize":"bad"}), true).is_err());
        let guest = super::parse_child_webview_options(&json!({"visible":false}), false).unwrap();
        assert!(guest.extra_config.get("visible").is_none());
        assert!(super::parse_child_webview_options(&json!({"unknown":true}), true).is_err());
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
}
