use std::sync::atomic::Ordering;
use std::time::{Duration, Instant};
use serde_json::{Value, json};
use crate::model::Submission;
use super::{LocalRuntime, UserEvent, CALLBACK_COMPLETION};

pub(super) struct PendingEvaluation {
    label: String,
    document_id: u64,
    key: String,
    script: Option<String>,
    nonce: Option<String>,
    started: bool,
    in_flight: bool,
    next_poll: Instant,
}

pub(super) const BOOTSTRAP: &str = include_str!("script_evaluation.js");

fn start_script(key: &str, nonce: &str, script: &str) -> String {
    let nonce = serde_json::to_string(nonce).unwrap();
    let key = serde_json::to_string(key).unwrap();
    format!("(()=>{{if(globalThis.__NETWIV_EVALUATE__('prepare',{nonce},{key})!=='started')return '{{\"kind\":\"cancelled\"}}';\
        try{{globalThis.__NETWIV_EVALUATE__('resolve',{nonce},{key},(\n{script}\n));}}\
        catch(error){{globalThis.__NETWIV_EVALUATE__('reject',{nonce},{key},error);}}\
        return globalThis.__NETWIV_EVALUATE__('poll',{nonce},{key})??'started';}})()")
}

fn parse_result(result: &str) -> Result<Option<Value>, String> {
    // Wry serializes the completion value. The value is itself a JSON string
    // to avoid engine-specific treatment of objects, undefined and exceptions.
    let value: Value = serde_json::from_str(result).map_err(|_| "WebView returned an invalid script result")?;
    if value.is_null() { return Ok(None); }
    let value: Value = serde_json::from_str(value.as_str().ok_or("WebView returned a non-string script result")?)
        .map_err(|_| "WebView returned invalid script result JSON")?;
    match value.get("kind").and_then(Value::as_str) {
        Some("result") if value.get("value").is_some() => Ok(Some(value)),
        Some("error") if value.get("name").is_some_and(Value::is_string) && value.get("message").is_some_and(Value::is_string) => Ok(Some(value)),
        Some("cancelled") => Ok(Some(value)),
        _ => Err("WebView returned an invalid script result envelope".into()),
    }
}

impl LocalRuntime {
    pub(super) fn begin_script_evaluation(&mut self, submission: &Submission, request_id: u64) -> Result<(), String> {
        if !submission.is_backend { return Err("Script results are available only to the managed backend".into()); }
        if submission.document_id == 0 { return Err("Script results require a live document".into()); }
        if self.pending_evaluations.len() >= 64 { return Err("Too many pending script evaluations".into()); }
        let script = submission.args.get("script").and_then(Value::as_str).ok_or("script must be a string")?;
        let mut bytes = [0u8; 32];
        getrandom::fill(&mut bytes).map_err(|_| "Could not create script result identifier")?;
        let key = format!("__netwiv_eval_{}", bytes.iter().map(|byte| format!("{byte:02x}")).collect::<String>());
        self.pending_evaluations.insert(request_id, PendingEvaluation {
            label: submission.webview_label.clone(), document_id: submission.document_id, key,
            script: Some(script.to_owned()), nonce: None, started: false, in_flight: false, next_poll: Instant::now(),
        });
        self.submit_script_evaluation(request_id, "globalThis.__NETWIV_EVALUATE__?.('nonce')");
        Ok(())
    }

    fn evaluation_is_current(&self, pending: &PendingEvaluation) -> bool {
        self.webviews.get(&pending.label).is_some_and(|view| view.document_id.load(Ordering::Acquire) == pending.document_id)
    }

    fn submit_script_evaluation(&mut self, request_id: u64, code: &str) {
        let Some(pending) = self.pending_evaluations.get(&request_id) else { return; };
        if !self.evaluation_is_current(pending) { self.cancel_script_evaluation(request_id); return; }
        let label = pending.label.clone();
        let shared = self.shared.clone();
        self.pending_evaluations.get_mut(&request_id).unwrap().in_flight = true;
        let result = self.webviews[&label].native.evaluate_script_with_callback(code, move |result| {
            let _ = shared.enqueue(UserEvent::ScriptEvaluationResult { request_id, result });
        });
        if let Err(error) = result {
            self.finish_script_evaluation(request_id, json!({"ok":false,"error":format!("Could not evaluate script: {error}")}));
        }
    }

    pub(super) fn poll_script_evaluations(&mut self) {
        let ids: Vec<_> = self.pending_evaluations.keys().copied().collect();
        for request_id in ids {
            let Some(pending) = self.pending_evaluations.get(&request_id) else { continue; };
            if !self.evaluation_is_current(pending) { self.cancel_script_evaluation(request_id); continue; }
            if pending.in_flight || Instant::now() < pending.next_poll { continue; }
            let key = serde_json::to_string(&pending.key).unwrap();
            let nonce = serde_json::to_string(&pending.nonce).unwrap();
            let code = format!("globalThis.__NETWIV_EVALUATE__('poll',{nonce},{key})");
            self.submit_script_evaluation(request_id, &code);
        }
    }

    pub(super) fn receive_script_evaluation(&mut self, request_id: u64, result: &str) {
        let Some(pending) = self.pending_evaluations.get(&request_id) else { return; };
        if !self.evaluation_is_current(pending) { self.cancel_script_evaluation(request_id); return; }
        if pending.script.is_some() {
            let nonce = serde_json::from_str::<String>(result).ok()
                .filter(|nonce| nonce.len() == 64 && nonce.bytes().all(|byte| byte.is_ascii_hexdigit()));
            let Some(nonce) = nonce else { self.cancel_script_evaluation(request_id); return; };
            let pending = self.pending_evaluations.get_mut(&request_id).unwrap();
            let code = start_script(&pending.key, &nonce, &pending.script.take().unwrap());
            pending.nonce = Some(nonce);
            self.submit_script_evaluation(request_id, &code);
            return;
        }
        if !pending.started {
            if serde_json::from_str::<String>(result).ok().as_deref() == Some("started") {
                let pending = self.pending_evaluations.get_mut(&request_id).unwrap();
                pending.started = true;
                pending.in_flight = false;
                return;
            }
            if let Ok(Some(value)) = parse_result(result) {
                if value["kind"] == "cancelled" { self.cancel_script_evaluation(request_id); }
                else { self.finish_script_evaluation(request_id, json!({"ok":true,"isBinary":false,"value":value})); }
                return;
            }
            // Wry does not expose every engine's parse/execution diagnostic.
            // Do not label an absent acknowledgement as a definite SyntaxError.
            self.finish_script_evaluation(request_id, json!({"ok":true,"isBinary":false,"value":{
                "kind":"error","name":"JavaScriptEvaluationError",
                "message":"The WebView could not execute the JavaScript expression. Check its syntax.","stack":null}}));
            return;
        }
        match parse_result(result) {
            Ok(None) => {
                let pending = self.pending_evaluations.get_mut(&request_id).unwrap();
                pending.in_flight = false;
                pending.next_poll = Instant::now() + Duration::from_millis(25);
            }
            Ok(Some(value)) if value["kind"] == "cancelled" => self.cancel_script_evaluation(request_id),
            Ok(Some(value)) => self.finish_script_evaluation(request_id, json!({"ok":true,"value":value,"isBinary":false})),
            Err(error) => self.finish_script_evaluation(request_id, json!({"ok":false,"error":error})),
        }
    }

    pub(super) fn cancel_script_evaluation(&mut self, request_id: u64) {
        self.finish_script_evaluation(request_id, json!({"ok":false,"cancelled":true,"error":"Script evaluation was cancelled"}));
    }

    pub(super) fn cancel_all_script_evaluations(&mut self) {
        let ids: Vec<_> = self.pending_evaluations.keys().copied().collect();
        for id in ids { self.cancel_script_evaluation(id); }
    }

    fn finish_script_evaluation(&mut self, request_id: u64, payload: Value) {
        let Some(pending) = self.pending_evaluations.remove(&request_id) else { return; };
        if self.evaluation_is_current(&pending) {
            let key = serde_json::to_string(&pending.key).unwrap();
            let nonce = serde_json::to_string(&pending.nonce).unwrap();
            let _ = self.webviews[&pending.label].native.evaluate_script(&format!("globalThis.__NETWIV_EVALUATE__('cancel',{nonce},{key})"));
        }
        self.shared.callback(CALLBACK_COMPLETION, request_id, &payload, &[]);
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn pending_result_error_and_malformed_results() {
        assert!(parse_result("null").unwrap().is_none());
        for value in [json!({"kind":"result","value":null}), json!({"kind":"result","value":{"answer":42}}),
            json!({"kind":"error","name":"TypeError","message":"failure","stack":"frame"})] {
            let response = serde_json::to_string(&value.to_string()).unwrap();
            assert_eq!(parse_result(&response).unwrap(), Some(value));
        }
        for value in ["undefined", "{}", "\"not json\"", "\"{}\""] { assert!(parse_result(value).is_err()); }
    }
    #[test]
    fn expression_is_injected_and_identifiers_are_json_encoded() {
        let script = start_script("key\"", "nonce", "(()=>{\nreturn 'value';})()");
        assert!(script.contains("\"key\\\"\""));
        assert!(script.contains("(()=>{\nreturn 'value';})()"));
        assert!(!script.contains("eval("));
    }
}
