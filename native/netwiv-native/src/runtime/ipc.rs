// WebKitGTK's message signal has no sender-frame metadata. Authenticate the
// main document before attributing its native URL or epoch to a message.
use std::sync::atomic::Ordering;
use serde::Deserialize;
use super::{LocalRuntime, UserEvent};

#[derive(Default)]
pub(super) struct DocumentIpc {
    document_id: u64,
    capability: String,
    source: String,
    provisional_navigation: bool,
    nonce: Option<String>,
    navigation: u64,
    failed_navigation: bool,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct Envelope {
    capability: String,
    document_id: u64,
    payload: String,
}

impl DocumentIpc {
    pub(super) fn commit(&mut self, document_id: u64, source: &str) -> Result<(), String> {
        self.invalidate();
        let mut bytes = [0u8; 32];
        getrandom::fill(&mut bytes).map_err(|_| "could not create a document IPC capability".to_string())?;
        self.capability = bytes.iter().map(|byte| format!("{byte:02x}")).collect();
        self.document_id = document_id;
        self.source = source.to_owned();
        self.provisional_navigation = false;
        Ok(())
    }

    pub(super) fn invalidate(&mut self) {
        self.document_id = 0;
        self.capability.clear();
        self.source.clear();
        self.provisional_navigation = true;
        self.nonce = None;
        self.failed_navigation = false;
    }

    pub(super) fn start_navigation(&mut self) {
        self.provisional_navigation = true;
        self.navigation = self.navigation.wrapping_add(1);
        self.failed_navigation = false;
    }

    pub(super) fn resume_failed_navigation(&mut self, nonce: &str, navigation: u64) {
        // A failed provisional navigation may leave the old document alive.
        // No committed replacement is allowed to reuse this capability.
        if self.navigation == navigation && self.nonce.as_deref() == Some(nonce) && self.document_id != 0 {
            self.provisional_navigation = false;
            self.failed_navigation = true;
        }
    }

    pub(super) fn update_source(&mut self, source: &str) {
        if !self.provisional_navigation && same_origin(&self.source, source) { self.source = source.to_owned(); }
    }

    pub(super) fn finish_loading_source(&mut self, source: &str) {
        if !self.failed_navigation { self.update_source(source); }
    }

    pub(super) fn authorize(&self, capability: &str, document_id: u64, source: &str) -> bool {
        document_id != 0 && document_id == self.document_id &&
            capability.len() == self.capability.len() &&
            capability.as_bytes().iter().zip(self.capability.as_bytes()).fold(0, |diff, (a, b)| diff | (a ^ b)) == 0 &&
            same_origin(&self.source, source)
    }

    pub(super) fn message(&self, json: &str) -> Option<(u64, String, String)> {
        let message: Envelope = serde_json::from_str(json).ok()?;
        // A provisional target is not the document that owns this capability.
        // Keep the still-live committed document usable until actual commit.
        let source = &self.source;
        self.authorize(&message.capability, message.document_id, source)
            .then(|| (message.document_id, message.payload, source.to_owned()))
    }

    pub(super) fn protocol_source(&self, request: &http::Request<Vec<u8>>) -> Option<(u64, String)> {
        let capability = request.headers().get("netwiv-document-capability")?.to_str().ok()?;
        let epoch = request.headers().get("netwiv-document-id")?.to_str().ok()?.parse().ok()?;
        self.authorize(capability, epoch, &self.source).then(|| (epoch, self.source.clone()))
    }

    pub(super) fn may_activate(&self, epoch: u64) -> bool {
        !self.provisional_navigation && epoch != 0 && epoch == self.document_id
    }

    pub(super) fn navigation(&self) -> u64 { self.navigation }
}

fn same_origin(left: &str, right: &str) -> bool {
    let (Ok(left), Ok(right)) = (url::Url::parse(left), url::Url::parse(right)) else { return false; };
    // Custom schemes have opaque URL origins; compare their actual authorities.
    left.scheme() == right.scheme() && left.host_str().is_some() && left.host_str() == right.host_str() &&
        left.port_or_known_default() == right.port_or_known_default()
}

impl LocalRuntime {
    pub(super) fn query_ipc_nonce(&self, label: &str, document_id: u64, failed_navigation: Option<u64>) -> Result<(), String> {
        let Some(view) = self.webviews.get(label) else { return Ok(()); };
        if view.document_id.load(Ordering::Acquire) != document_id { return Ok(()); }
        let navigation = {
            let documents = self.shared.webview_origins.lock().map_err(|_| "webview origin lock is poisoned")?;
            let Some(document) = documents.get(label) else { return Ok(()); };
            let ipc = document.ipc.lock().map_err(|_| "document IPC lock is poisoned")?;
            if let Some(expected) = failed_navigation {
                if ipc.navigation != expected { return Ok(()); }
            } else if !ipc.may_activate(document_id) { return Ok(()); }
            ipc.navigation
        };
        let shared = self.shared.clone();
        let label = label.to_owned();
        view.native.evaluate_script_with_callback("window.__NETWIV_IPC_NONCE__?.()", move |nonce| {
            let _ = shared.enqueue(UserEvent::IpcNonce { label: label.clone(), document_id, navigation, failed_navigation: failed_navigation.is_some(), nonce });
        }).map_err(|error| format!("could not query the main document IPC nonce: {error}"))
    }

    pub(super) fn activate_ipc(&self, label: &str, document_id: u64, navigation: u64, failed_navigation: bool, nonce_json: &str) -> Result<(), String> {
        let Ok(nonce) = serde_json::from_str::<String>(nonce_json) else { return Ok(()); };
        if nonce.len() != 64 || !nonce.bytes().all(|byte| byte.is_ascii_hexdigit()) { return Ok(()); }
        let script = {
            let documents = self.shared.webview_origins.lock().map_err(|_| "webview origin lock is poisoned")?;
            let Some(document) = documents.get(label) else { return Ok(()); };
            let mut ipc = document.ipc.lock().map_err(|_| "document IPC lock is poisoned")?;
            if ipc.navigation != navigation { return Ok(()); }
            if failed_navigation { ipc.resume_failed_navigation(&nonce, navigation); }
            if !ipc.may_activate(document_id) { return Ok(()); }
            ipc.nonce = Some(nonce.clone());
            format!("window.__NETWIV_ACTIVATE_IPC__?.({},{},{});", serde_json::to_string(&nonce).unwrap(),
                document_id, serde_json::to_string(&ipc.capability).unwrap())
        };
        self.eval(label, document_id, &script)
    }

    pub(super) fn guard_document_eval(&self, label: &str, script: &str) -> Result<Option<String>, String> {
        let documents = self.shared.webview_origins.lock().map_err(|_| "webview origin lock is poisoned")?;
        let Some(document) = documents.get(label) else { return Ok(None); };
        let ipc = document.ipc.lock().map_err(|_| "document IPC lock is poisoned")?;
        let Some(nonce) = &ipc.nonce else { return Ok(None); };
        Ok(Some(format!("(()=>{{if(window.__NETWIV_IPC_NONCE__?.()!=={})return;\n{script}\n}})();", serde_json::to_string(nonce).unwrap())))
    }
}

pub(super) const BOOTSTRAP: &str = include_str!("ipc.js");

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn rejects_raw_forged_stale_cross_origin_and_freezes_provisional_source() {
        let mut ipc = DocumentIpc::default();
        ipc.commit(1, "netwiv-blazor://localhost/").unwrap();
        let cap = ipc.capability.clone();
        let message = serde_json::json!({"capability":cap,"documentId":1,"payload":"trusted"}).to_string();
        ipc.update_source("netwiv-blazor://localhost/next");
        assert_eq!(ipc.message(&message), Some((1, "trusted".into(), "netwiv-blazor://localhost/next".into())));
        assert!(ipc.message("trusted").is_none());
        assert!(ipc.message(&message.replace(&cap, "forged")).is_none());
        assert!(!ipc.authorize(&cap, 1, "other-scheme://localhost/"));
        assert!(!ipc.authorize(&cap, 1, "netwiv-blazor://foreign/"));
        ipc.start_navigation();
        ipc.update_source("https://remote.invalid/pending");
        assert_eq!(ipc.message(&message),
            Some((1, "trusted".into(), "netwiv-blazor://localhost/next".into())));
        ipc.nonce = Some("native-read-nonce".into());
        ipc.resume_failed_navigation("different-document", ipc.navigation);
        assert!(!ipc.may_activate(1));
        ipc.resume_failed_navigation("native-read-nonce", ipc.navigation);
        assert!(ipc.message(&message).is_some());
        ipc.finish_loading_source("netwiv-blazor://localhost/failed-target");
        assert_eq!(ipc.message(&message).unwrap().2, "netwiv-blazor://localhost/next");
        ipc.commit(2, "netwiv-blazor://localhost/").unwrap();
        assert_ne!(cap, ipc.capability);
        assert!(ipc.message(&message).is_none());
        ipc.invalidate();
        assert!(!ipc.may_activate(2));
    }

    #[test]
    fn protocol_authorization_uses_native_document_and_tracks_same_origin_history() {
        let mut ipc = DocumentIpc::default();
        ipc.commit(7, "tauri://localhost/").unwrap();
        let request = http::Request::builder().header("netwiv-document-capability", &ipc.capability)
            .header("netwiv-document-id", "7").body(vec![]).unwrap();
        ipc.update_source("tauri://localhost/route");
        assert_eq!(ipc.protocol_source(&request), Some((7, "tauri://localhost/route".into())));
        ipc.update_source("https://remote.invalid/");
        assert_eq!(ipc.protocol_source(&request), Some((7, "tauri://localhost/route".into())));
        assert!(ipc.protocol_source(&http::Request::new(vec![])).is_none());
        let forged = http::Request::builder().header("netwiv-document-capability", "forged")
            .header("netwiv-document-id", "7").body(vec![]).unwrap();
        assert!(ipc.protocol_source(&forged).is_none());
        let snapshot = ipc.protocol_source(&request).unwrap();
        ipc.start_navigation();
        ipc.update_source("tauri://localhost/privileged-pending-route");
        assert_eq!(ipc.protocol_source(&request), Some(snapshot.clone()));
        ipc.update_source("https://remote.invalid/pending");
        assert_eq!(ipc.protocol_source(&request), Some(snapshot.clone()));
        ipc.commit(8, "tauri://localhost/").unwrap();
        assert!(ipc.protocol_source(&request).is_none());
        assert_eq!(snapshot, (7, "tauri://localhost/route".into()));
        ipc.invalidate();
        assert!(!ipc.may_activate(8));
    }

    #[test]
    fn delayed_failed_navigation_callback_cannot_resume_a_new_navigation() {
        let mut ipc = DocumentIpc::default();
        ipc.commit(1, "tauri://localhost/").unwrap();
        ipc.nonce = Some("retained-document".into());
        ipc.start_navigation();
        let failed_navigation = ipc.navigation;
        ipc.start_navigation();
        ipc.resume_failed_navigation("retained-document", failed_navigation);
        assert!(!ipc.may_activate(1));
        ipc.resume_failed_navigation("replacement-document", ipc.navigation);
        assert!(!ipc.may_activate(1));
        ipc.resume_failed_navigation("retained-document", ipc.navigation);
        assert!(ipc.may_activate(1));
        ipc.commit(2, "tauri://localhost/").unwrap();
        ipc.start_navigation();
        ipc.resume_failed_navigation("retained-document", ipc.navigation);
        assert!(!ipc.may_activate(2));
    }
}
