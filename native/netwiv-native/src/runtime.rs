mod window;
mod webview;
mod commands;
mod menus;
mod protocol;
mod options;
mod platform;
#[cfg(target_os = "linux")]
mod dialogs;
#[cfg(test)]
mod tests;

use std::collections::HashMap;
use std::ffi::c_void;
use std::path::PathBuf;
use std::sync::{Arc, Mutex};
use std::sync::atomic::{AtomicU8, AtomicU64, Ordering};
use std::time::{Duration, Instant};
use http::Response;
use serde_json::{Value, json};
use tao::event::{Event, WindowEvent};
use tao::event_loop::{ControlFlow, EventLoopBuilder, EventLoopProxy, EventLoopWindowTarget};
use tao::window::{Theme, Window, WindowId};
use wry::{RequestAsyncResponder, WebContext, WebView};
use crate::abi::NetWivCallback;
use crate::model::{RunConfig, Submission, validate_config};
use crate::relative_bounds::RelativeBounds;
use crate::resources::{ChannelTarget, NativeResources};
use self::menus::detach_menu;
use self::options::theme_name;
use self::platform::platform_backend;

use tao::platform::run_return::EventLoopExtRunReturn;

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

}

pub(crate) enum UserEvent {
    CancelRequest { request_id: u64 },
    #[cfg(target_os = "linux")]
    DialogCompletion { request_id: u64, result: Result<Value, String> },
    #[cfg(target_os = "linux")]
    DialogDocumentChanged { webview_label: String, document_id: u64 },
    #[cfg(target_os = "macos")]
    DataStoreCompletion { request_id: u64, result: Result<Value, String> },
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
    #[cfg(target_os = "linux")]
    pending_dialogs: HashMap<u64, dialogs::PendingNativeDialog>,
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
        #[cfg(target_os = "linux")]
        pending_dialogs: HashMap::new(),
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
    #[cfg(target_os = "linux")]
    runtime.cancel_all_dialogs("native runtime stopped before dialog completed");
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
                    .and_then(|submission| self.invoke(target, &submission, &binary, request_id));
                match result {
                    #[cfg(any(target_os = "macos", target_os = "linux"))]
                    Ok(InvokeResult::Pending) => {}
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
            UserEvent::CancelRequest { request_id } => {
                #[cfg(target_os = "linux")]
                self.cancel_dialog_request(request_id);
                // Other platforms retain their synchronous rfd behavior.
                #[cfg(not(target_os = "linux"))]
                let _ = request_id;
                Ok(())
            }
            #[cfg(target_os = "linux")]
            UserEvent::DialogCompletion { request_id, result } => {
                self.complete_dialog_response(request_id, result);
                Ok(())
            }
            #[cfg(target_os = "linux")]
            UserEvent::DialogDocumentChanged { webview_label, document_id } => {
                self.cancel_dialogs_for_document(&webview_label, document_id);
                Ok(())
            }
            #[cfg(target_os = "macos")]
            UserEvent::DataStoreCompletion { request_id, result } => {
                let payload = match result {
                    Ok(value) => json!({"ok":true,"value":value,"isBinary":false}),
                    Err(error) => json!({"ok":false,"error":error}),
                };
                self.shared.callback(CALLBACK_COMPLETION, request_id, &payload, &[]);
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

    fn remove_webview(&mut self, label: &str) -> Result<(), String> {
        #[cfg(target_os = "linux")]
        self.cancel_dialogs_for_webview(label);
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
        #[cfg(target_os = "linux")]
        self.cancel_dialogs_for_window(label);
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
        #[cfg(target_os = "linux")]
        self.cancel_all_dialogs("native runtime stopped before dialog completed");
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

}

enum InvokeResult {
    #[cfg(any(target_os = "macos", target_os = "linux"))]
    Pending,
    Json(Value),
    #[allow(dead_code)]
    Binary(Vec<u8>),
}

fn sorted<'a>(values: impl Iterator<Item = &'a String>) -> Vec<String> {
    let mut result: Vec<_> = values.cloned().collect();
    result.sort();
    result
}
