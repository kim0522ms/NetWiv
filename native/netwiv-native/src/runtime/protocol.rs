use std::sync::Arc;
use std::sync::atomic::{AtomicU64, Ordering};
use http::{HeaderName, HeaderValue, Response};
use serde_json::{Value, json};
use wry::RequestAsyncResponder;
use crate::model::AssetResponseMetadata;
use super::{Shared, ProtocolDocument, UserEvent, STATE_STOPPED, CALLBACK_ASSET};

impl Shared {
    pub(super) fn next_document_id(&self) -> u64 {
        self.next_document.fetch_add(1, Ordering::Relaxed)
    }

    pub(super) fn register_webview_origin(
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

    pub(super) fn remove_webview_origin(&self, label: &str) {
        if let Ok(mut origins) = self.webview_origins.lock() {
            origins.remove(label);
        }
    }

    pub(super) fn webview_parent(&self, label: &str) -> Option<String> {
        self.webview_origins
            .lock()
            .ok()?
            .get(label)
            .map(|document| document.window_label.clone())
    }

    pub(super) fn reparent_document(&self, label: &str, destination: &str) -> Result<(), String> {
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

    pub(super) fn request_asset(
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

    pub(super) fn drain_assets(&self) {
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

pub(super) fn build_asset_response(metadata: &str, body: Vec<u8>) -> Result<Response<Vec<u8>>, String> {
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

pub(super) fn bridge_script(
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

pub(super) fn webview_origin(initial_url: &str, use_https_scheme: bool) -> String {
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
