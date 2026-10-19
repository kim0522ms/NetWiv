use std::sync::atomic::Ordering;

use serde_json::{Value, json};

use crate::model::Submission;
use super::{CALLBACK_COMPLETION, InvokeResult, LocalRuntime, UserEvent};

pub(super) struct PendingNativeDialog {
    _dialog: crate::dialog::PendingDialog,
    scope: DialogScope,
}

struct DialogScope {
    parent_window: String,
    source: Option<SourceDocument>,
}

struct SourceDocument {
    webview: String,
    document_id: u64,
}

impl DialogScope {
    fn invalid_reason(&self, parent_exists: bool, current_document: Option<u64>) -> Option<&'static str> {
        if !parent_exists { return Some("dialog parent window was closed"); }
        if let Some(source) = &self.source {
            let Some(current) = current_document else { return Some("dialog source webview was closed"); };
            if source.document_id != 0 && source.document_id != current {
                return Some("dialog source document changed");
            }
        }
        None
    }

    fn document_changed(&self, webview: &str, document_id: u64) -> bool {
        self.source.as_ref().is_some_and(|source|
            source.webview == webview && source.document_id != 0 && source.document_id != document_id)
    }
}

impl LocalRuntime {
    pub(super) fn begin_dialog(&mut self, submission: &Submission, request_id: u64) -> Result<InvokeResult, String> {
        if self.pending_dialogs.contains_key(&request_id) { return Err("dialog request ID is already pending".into()); }
        let parent_window = submission.window_label.as_deref()
            .or_else(|| self.webviews.get(&submission.webview_label).map(|view| view.window_label.as_str()))
            .ok_or("dialog request has no parent window")?.to_owned();
        let parent = self.windows.get(&parent_window).ok_or("dialog parent window was not found")?;
        let source = self.webviews.contains_key(&submission.webview_label).then(|| SourceDocument {
            webview: submission.webview_label.clone(), document_id: submission.document_id,
        });
        let shared = self.shared.clone();
        let dialog = crate::dialog::begin(&submission.command, &submission.args, &parent.window, move |result| {
            // GTK runs this callback on its UI thread. Only queue an event: no
            // runtime borrow or managed callback may outlive the pending request.
            let _ = shared.enqueue(UserEvent::DialogCompletion { request_id, result });
        })?;
        self.pending_dialogs.insert(request_id, PendingNativeDialog {
            _dialog: dialog, scope: DialogScope { parent_window, source },
        });
        Ok(InvokeResult::Pending)
    }

    pub(super) fn complete_dialog_response(&mut self, request_id: u64, result: Result<Value, String>) {
        let Some(pending) = self.pending_dialogs.get(&request_id) else { return; };
        let scope = &pending.scope;
        let current_document = scope.source.as_ref().and_then(|source|
            self.webviews.get(&source.webview).map(|view| view.document_id.load(Ordering::Acquire)));
        // Page-start changes the document atomic before queuing its event. Check
        // it here as well so an already queued response cannot enter a new page.
        let result = match scope.invalid_reason(self.windows.contains_key(&scope.parent_window), current_document) {
            Some(error) => Err(error.to_owned()),
            None => result,
        };
        self.finish_dialog(request_id, response_payload(result));
    }

    pub(super) fn cancel_dialog_request(&mut self, request_id: u64) {
        self.finish_dialog(request_id, json!({"ok":false,"cancelled":true,"error":"dialog request was cancelled"}));
    }

    pub(super) fn cancel_dialogs_for_window(&mut self, window: &str) {
        self.cancel_dialogs_matching(|scope| scope.parent_window == window, "dialog parent window was closed");
    }

    pub(super) fn cancel_dialogs_for_webview(&mut self, webview: &str) {
        self.cancel_dialogs_matching(|scope| scope.source.as_ref().is_some_and(|source| source.webview == webview),
            "dialog source webview was closed");
    }

    pub(super) fn cancel_dialogs_for_document(&mut self, webview: &str, document_id: u64) {
        // A page-load signal can queue behind a later navigation. Use the live
        // document identity so an older queued event cannot cancel a new page's
        // dialog. Completion checks the same atomic independently.
        let current = self.webviews.get(webview).map(|view| view.document_id.load(Ordering::Acquire)).unwrap_or(document_id);
        self.cancel_dialogs_matching(|scope| scope.document_changed(webview, current), "dialog source document changed");
    }

    pub(super) fn cancel_all_dialogs(&mut self, error: &str) {
        self.cancel_dialogs_matching(|_| true, error);
    }

    fn cancel_dialogs_matching(&mut self, predicate: impl Fn(&DialogScope) -> bool, error: &str) {
        let ids = self.pending_dialogs.iter().filter_map(|(id, pending)| predicate(&pending.scope).then_some(*id))
            .collect::<Vec<_>>();
        for request_id in ids { self.finish_dialog(request_id, response_payload(Err(error.to_owned()))); }
    }

    fn finish_dialog(&mut self, request_id: u64, payload: Value) {
        let Some(pending) = self.pending_dialogs.remove(&request_id) else { return; };
        // Destroy/disconnect the GTK object before notifying managed code, which
        // can synchronously request shutdown or submit another invocation.
        drop(pending);
        self.shared.callback(CALLBACK_COMPLETION, request_id, &payload, &[]);
    }
}

fn response_payload(result: Result<Value, String>) -> Value {
    match result {
        Ok(value) => json!({"ok":true,"value":value,"isBinary":false}),
        Err(error) => json!({"ok":false,"error":error}),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn document_and_parent_lifetimes_are_independent_of_dialog_response() {
        let scope = DialogScope { parent_window: "parent".into(), source: Some(SourceDocument { webview: "child".into(), document_id: 41 }) };
        assert_eq!(scope.invalid_reason(true, Some(41)), None);
        assert_eq!(scope.invalid_reason(false, Some(41)), Some("dialog parent window was closed"));
        assert_eq!(scope.invalid_reason(true, None), Some("dialog source webview was closed"));
        assert_eq!(scope.invalid_reason(true, Some(42)), Some("dialog source document changed"));
        assert!(scope.document_changed("child", 42));
        assert!(!scope.document_changed("child", 41));
        assert!(!scope.document_changed("other", 42));
        let native = DialogScope { parent_window: "parent".into(), source: None };
        assert_eq!(native.invalid_reason(true, None), None);
        assert!(!native.document_changed("child", 42));
        let backend_view = DialogScope { parent_window: "parent".into(), source: Some(SourceDocument { webview: "child".into(), document_id: 0 }) };
        assert!(!backend_view.document_changed("child", 42));
        assert_eq!(backend_view.invalid_reason(true, Some(42)), None);
        assert_eq!(backend_view.invalid_reason(true, None), Some("dialog source webview was closed"));
    }

    #[test]
    fn user_dialog_cancel_is_a_successful_value_and_lifecycle_errors_are_not_token_cancellation() {
        assert_eq!(response_payload(Ok(Value::Null)), json!({"ok":true,"value":null,"isBinary":false}));
        assert_eq!(response_payload(Ok(json!("Cancel"))), json!({"ok":true,"value":"Cancel","isBinary":false}));
        assert_eq!(response_payload(Err("dialog parent window was closed".into())), json!({"ok":false,"error":"dialog parent window was closed"}));
    }
}
