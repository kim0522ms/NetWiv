use std::collections::HashMap;
use std::time::{Duration, Instant};

#[cfg(target_os = "windows")]
use serde::Deserialize;
use url::Url;

const MAX_TICKETS: usize = 64;
const MAX_DOCUMENT_TICKETS: usize = 16;
const TICKET_LIFETIME: Duration = Duration::from_secs(30);
const DOCUMENT_NONCE_LENGTH: usize = 32;
#[cfg(target_os = "windows")]
const FILE_PATH_MESSAGE_MARKER: &str = "__NETWIV_FILE_PATH__";
#[cfg(target_os = "windows")]
const FILE_PATH_MESSAGE_PREFIX: &str = "{\"__NETWIV_FILE_PATH__\":\"";
#[cfg(target_os = "windows")]
const MAX_MESSAGE_LENGTH: usize = 4096;

#[derive(Clone, Debug)]
struct Ticket {
    webview: String,
    source: Url,
    document_id: u64,
    nonce: String,
    generation: u64,
    issued_at: Instant,
}

#[derive(Default)]
pub(super) struct FilePathTickets {
    entries: HashMap<String, Ticket>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(super) enum FilePathError {
    Unavailable,
    #[cfg(target_os = "windows")]
    NotSupported,
}

#[cfg(target_os = "windows")]
impl FilePathError {
    pub(super) fn code(self) -> &'static str {
        match self {
            Self::Unavailable => "FilePathUnavailable",
            #[cfg(target_os = "windows")]
            Self::NotSupported => "FilePathNotSupported",
        }
    }
}

#[cfg(target_os = "windows")]
#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub(super) struct FilePathRequest {
    pub nonce: String,
    pub generation: u64,
}

#[cfg(target_os = "windows")]
#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub(super) struct FilePathMessage {
    pub ticket: String,
    pub nonce: String,
    pub generation: u64,
    pub callback: u32,
    pub error: u32,
}

impl FilePathTickets {
    pub(super) fn issue(
        &mut self,
        webview: &str,
        source: &str,
        document_id: u64,
        nonce: &str,
        generation: u64,
        now: Instant,
    ) -> Result<String, FilePathError> {
        if document_id == 0
            || nonce.len() != DOCUMENT_NONCE_LENGTH
            || !nonce.bytes().all(|byte| byte.is_ascii_hexdigit())
        {
            return Err(FilePathError::Unavailable);
        }
        let source = Url::parse(source).map_err(|_| FilePathError::Unavailable)?;
        let mut bytes = [0; 32];
        getrandom::fill(&mut bytes).map_err(|_| FilePathError::Unavailable)?;
        let token = bytes
            .iter()
            .map(|byte| format!("{byte:02x}"))
            .collect::<String>();
        self.insert(
            token.clone(),
            webview,
            source,
            document_id,
            nonce,
            generation,
            now,
        );
        Ok(token)
    }

    fn insert(
        &mut self,
        token: String,
        webview: &str,
        source: Url,
        document_id: u64,
        nonce: &str,
        generation: u64,
        now: Instant,
    ) {
        self.prune(now);
        let same_document =
            |ticket: &Ticket| ticket.webview == webview && ticket.document_id == document_id;
        while self
            .entries
            .values()
            .filter(|ticket| same_document(ticket))
            .count()
            >= MAX_DOCUMENT_TICKETS
        {
            self.remove_oldest(same_document);
        }
        if self.entries.len() >= MAX_TICKETS {
            self.remove_oldest(|_| true);
        }
        self.entries.insert(
            token,
            Ticket {
                webview: webview.to_owned(),
                source,
                document_id,
                nonce: nonce.to_owned(),
                generation,
                issued_at: now,
            },
        );
    }

    pub(super) fn consume(
        &mut self,
        token: &str,
        webview: &str,
        source: &str,
        document_id: u64,
        nonce: &str,
        generation: u64,
        now: Instant,
    ) -> Result<(), FilePathError> {
        self.prune(now);
        let ticket = self
            .entries
            .remove(token)
            .ok_or(FilePathError::Unavailable)?;
        let source = Url::parse(source).map_err(|_| FilePathError::Unavailable)?;
        if ticket.webview != webview
            || ticket.source != source
            || ticket.document_id != document_id
            || ticket.nonce != nonce
            || ticket.generation != generation
        {
            return Err(FilePathError::Unavailable);
        }
        Ok(())
    }

    // Navigation, close, and successful reparenting revoke preauthorized tickets.
    pub(super) fn clear_webview(&mut self, webview: &str) {
        self.entries.retain(|_, ticket| ticket.webview != webview);
    }

    fn prune(&mut self, now: Instant) {
        self.entries
            .retain(|_, ticket| now.saturating_duration_since(ticket.issued_at) < TICKET_LIFETIME);
    }

    fn remove_oldest(&mut self, predicate: impl Fn(&Ticket) -> bool) {
        let oldest = self
            .entries
            .iter()
            .filter(|(_, ticket)| predicate(ticket))
            .min_by_key(|(_, ticket)| ticket.issued_at)
            .map(|(token, _)| token.clone());
        if let Some(token) = oldest {
            self.entries.remove(&token);
        }
    }

    #[cfg(test)]
    fn insert_test_ticket(
        &mut self,
        token: &str,
        webview: &str,
        source: &str,
        document_id: u64,
        nonce: &str,
        generation: u64,
        now: Instant,
    ) {
        self.insert(
            token.to_owned(),
            webview,
            Url::parse(source).unwrap(),
            document_id,
            nonce,
            generation,
            now,
        );
    }
}

#[cfg(target_os = "windows")]
pub(super) struct WebMessageHook {
    webview: webview2_com::Microsoft::Web::WebView2::Win32::ICoreWebView2,
    token: i64,
}

#[cfg(target_os = "windows")]
impl WebMessageHook {
    pub(super) fn install(
        native: &wry::WebView,
        label: String,
        document_id: std::sync::Arc<std::sync::atomic::AtomicU64>,
        tickets: std::sync::Arc<std::sync::Mutex<FilePathTickets>>,
        shared: std::sync::Arc<super::Shared>,
    ) -> Result<Self, String> {
        use webview2_com::Microsoft::Web::WebView2::Win32::ICoreWebView2;
        use webview2_com::WebMessageReceivedEventHandler;
        use wry::WebViewExtWindows;

        let webview: ICoreWebView2 = native.webview().clone();
        let handler = WebMessageReceivedEventHandler::create(Box::new(move |_, args| {
            let Some(args) = args else {
                return Ok(());
            };
            let Some(message) = read_message(&args) else {
                return Ok(());
            };
            let source = read_source(&args);
            let current_document = document_id.load(std::sync::atomic::Ordering::Acquire);
            let ticket_result = tickets
                .lock()
                .map_err(|_| FilePathError::Unavailable)
                .and_then(|mut tickets| {
                    tickets.consume(
                        &message.ticket,
                        &label,
                        source.as_deref().unwrap_or_default(),
                        current_document,
                        &message.nonce,
                        message.generation,
                        Instant::now(),
                    )
                });
            let result = ticket_result.and_then(|()| read_file_path(&args));
            let (callback, value) = match result {
                Ok(path) => (message.callback, path),
                Err(error) => (message.error, error.code().to_owned()),
            };
            let script = format!(
                "window.__TAURI_INTERNALS__?.runFileCallback?.({},{},{},{});",
                js_string(&message.nonce),
                message.generation,
                callback,
                js_string(&value),
            );
            let _ = shared.enqueue(super::UserEvent::Eval {
                label: label.clone(),
                document_id: current_document,
                script,
            });
            Ok(())
        }));
        let mut token = 0;
        unsafe { webview.add_WebMessageReceived(&handler, &mut token) }
            .map_err(|_| "failed to register file path message handler".to_owned())?;
        Ok(Self { webview, token })
    }
}

#[cfg(target_os = "windows")]
impl Drop for WebMessageHook {
    fn drop(&mut self) {
        let _ = unsafe { self.webview.remove_WebMessageReceived(self.token) };
    }
}

#[cfg(target_os = "windows")]
fn js_string(value: &str) -> String {
    serde_json::to_string(value)
        .unwrap_or_else(|_| "\"\"".to_owned())
        .replace('\u{2028}', "\\u2028")
        .replace('\u{2029}', "\\u2029")
}

#[cfg(target_os = "windows")]
fn read_message(
    args: &webview2_com::Microsoft::Web::WebView2::Win32::ICoreWebView2WebMessageReceivedEventArgs,
) -> Option<FilePathMessage> {
    use webview2_com::CoTaskMemPWSTR;
    use windows::core::PWSTR;

    let mut message = PWSTR::null();
    unsafe { args.WebMessageAsJson(&mut message).ok()? };
    let message = CoTaskMemPWSTR::from(message).to_string();
    parse_message(&message)
}

#[cfg(target_os = "windows")]
pub(super) fn is_file_path_wrapper(message: &str) -> bool {
    if message.len() > MAX_MESSAGE_LENGTH {
        return message.starts_with(FILE_PATH_MESSAGE_PREFIX);
    }
    let Ok(value) = serde_json::from_str::<serde_json::Value>(message) else {
        return message.starts_with(FILE_PATH_MESSAGE_PREFIX);
    };
    let value = if let serde_json::Value::String(serialized) = value {
        let Ok(value) = serde_json::from_str::<serde_json::Value>(&serialized) else {
            return serialized.starts_with(FILE_PATH_MESSAGE_PREFIX);
        };
        value
    } else {
        value
    };
    value
        .as_object()
        .is_some_and(|object| object.contains_key(FILE_PATH_MESSAGE_MARKER))
}

#[cfg(target_os = "windows")]
fn parse_message(message: &str) -> Option<FilePathMessage> {
    if message.len() > MAX_MESSAGE_LENGTH {
        return None;
    }
    let serde_json::Value::String(serialized) = serde_json::from_str(message).ok()? else {
        return None;
    };
    let value: serde_json::Value = serde_json::from_str(&serialized).ok()?;
    let object = value.as_object()?;
    if object.len() != 1 {
        return None;
    }
    let serialized = object.get(FILE_PATH_MESSAGE_MARKER)?.as_str()?;
    serde_json::from_str(serialized).ok()
}

#[cfg(target_os = "windows")]
fn read_source(
    args: &webview2_com::Microsoft::Web::WebView2::Win32::ICoreWebView2WebMessageReceivedEventArgs,
) -> Option<String> {
    use webview2_com::CoTaskMemPWSTR;
    use windows::core::PWSTR;

    let mut source = PWSTR::null();
    unsafe { args.Source(&mut source).ok()? };
    Some(CoTaskMemPWSTR::from(source).to_string())
}

#[cfg(target_os = "windows")]
fn read_file_path(
    args: &webview2_com::Microsoft::Web::WebView2::Win32::ICoreWebView2WebMessageReceivedEventArgs,
) -> Result<String, FilePathError> {
    use webview2_com::CoTaskMemPWSTR;
    use webview2_com::Microsoft::Web::WebView2::Win32::{
        ICoreWebView2File, ICoreWebView2WebMessageReceivedEventArgs2,
    };
    use windows::core::{Interface, PWSTR};

    let args: ICoreWebView2WebMessageReceivedEventArgs2 =
        args.cast().map_err(|_| FilePathError::NotSupported)?;
    let objects = unsafe { args.AdditionalObjects() }.map_err(|_| FilePathError::NotSupported)?;
    let mut count = 0;
    unsafe { objects.Count(&mut count) }.map_err(|_| FilePathError::Unavailable)?;
    if count != 1 {
        return Err(FilePathError::Unavailable);
    }
    let object = unsafe { objects.GetValueAtIndex(0) }.map_err(|_| FilePathError::Unavailable)?;
    let file: ICoreWebView2File = object.cast().map_err(|_| FilePathError::Unavailable)?;
    let mut path = PWSTR::null();
    unsafe { file.Path(&mut path) }.map_err(|_| FilePathError::Unavailable)?;
    let path = CoTaskMemPWSTR::from(path).to_string();
    validate_file_path(&path)
}

#[cfg(target_os = "windows")]
fn validate_file_path(path: &str) -> Result<String, FilePathError> {
    let path = std::path::Path::new(path);
    if !path.is_absolute() {
        return Err(FilePathError::Unavailable);
    }
    Ok(path.to_string_lossy().into_owned())
}

#[cfg(all(test, target_os = "windows"))]
mod windows_path_tests {
    use super::{FilePathError, validate_file_path};

    #[test]
    fn only_absolute_paths_are_returned() {
        let file_text = r"C:\selected\fixture.txt";
        assert!(
            validate_file_path(file_text).as_deref() == Ok(file_text),
            "valid absolute file path was rejected"
        );
        assert_eq!(
            validate_file_path("relative-fixture.txt"),
            Err(FilePathError::Unavailable)
        );
        assert_eq!(validate_file_path(""), Err(FilePathError::Unavailable));
    }
}

#[cfg(all(test, target_os = "windows"))]
mod windows_message_tests {
    use super::{
        FILE_PATH_MESSAGE_PREFIX, MAX_MESSAGE_LENGTH, is_file_path_wrapper, parse_message,
    };

    fn webview_json(inner: &str) -> String {
        serde_json::to_string(
            &serde_json::json!({
                "__NETWIV_FILE_PATH__": inner
            })
            .to_string(),
        )
        .unwrap()
    }

    #[test]
    fn parses_string_wrapped_file_path_message_and_skips_normal_ipc() {
        let inner = r#"{"ticket":"ticket","nonce":"0123456789abcdef0123456789abcdef","generation":2,"callback":3,"error":4}"#;
        let webview_json = webview_json(inner);
        let ipc_body = serde_json::from_str::<String>(&webview_json).unwrap();

        assert!(is_file_path_wrapper(&ipc_body));
        assert!(is_file_path_wrapper(&webview_json));
        let message = parse_message(&webview_json).unwrap();
        assert_eq!(message.ticket, "ticket");
        assert_eq!(message.callback, 3);
        assert_eq!(message.error, 4);
        assert!(!is_file_path_wrapper(
            r#"{"cmd":"plugin:webview|get_file_path"}"#
        ));
        assert!(is_file_path_wrapper(
            r#"{"__NETWIV_FILE_PATH__":"malformed"}"#
        ));
    }

    #[test]
    fn rejects_invalid_string_wrappers_but_keeps_them_out_of_normal_ipc() {
        let malformed_inner = webview_json("not a file-path message");
        assert!(is_file_path_wrapper(&malformed_inner));
        assert!(parse_message(&malformed_inner).is_none());

        let extra_field = serde_json::json!({
            "__NETWIV_FILE_PATH__": "{}",
            "extra": true
        })
        .to_string();
        let malformed_wrapper = serde_json::to_string(&extra_field).unwrap();
        assert!(is_file_path_wrapper(&malformed_wrapper));
        assert!(parse_message(&malformed_wrapper).is_none());

        let malformed_root = r#"{"__NETWIV_FILE_PATH__":"malformed""#;
        assert!(is_file_path_wrapper(malformed_root));
        let malformed_nested = r#"{"cmd":"plugin:test","args":{"__NETWIV_FILE_PATH__":"malformed""#;
        assert!(!is_file_path_wrapper(malformed_nested));

        let oversized_wrapper = format!(
            "{}{}",
            FILE_PATH_MESSAGE_PREFIX,
            "x".repeat(MAX_MESSAGE_LENGTH)
        );
        assert!(is_file_path_wrapper(&oversized_wrapper));
        let oversized_command = serde_json::json!({
            "cmd": "plugin:test",
            "args": {"__NETWIV_FILE_PATH__": "x".repeat(MAX_MESSAGE_LENGTH)}
        })
        .to_string();
        assert!(oversized_command.len() > MAX_MESSAGE_LENGTH);
        assert!(!is_file_path_wrapper(&oversized_command));

        assert!(!is_file_path_wrapper("not JSON"));
        assert!(parse_message("not JSON").is_none());
    }
}

#[cfg(test)]
mod tests {
    use super::{
        FilePathError, FilePathTickets, MAX_DOCUMENT_TICKETS, MAX_TICKETS, TICKET_LIFETIME,
    };
    use std::time::{Duration, Instant};

    const SOURCE: &str = "https://example.test/app/index.html";
    const NONCE: &str = "0123456789abcdef0123456789abcdef";

    fn issue(store: &mut FilePathTickets, token: &str, now: Instant) {
        store.insert_test_ticket(token, "main", SOURCE, 7, NONCE, 2, now);
    }

    #[test]
    fn ticket_is_bound_to_source_document_nonce_and_generation_and_is_one_shot() {
        let now = Instant::now();
        let mut store = FilePathTickets::default();
        issue(&mut store, "ticket", now);

        assert_eq!(
            store.consume("ticket", "main", "https://other.test/", 7, NONCE, 2, now),
            Err(FilePathError::Unavailable)
        );
        assert_eq!(
            store.consume("ticket", "main", SOURCE, 7, NONCE, 2, now),
            Err(FilePathError::Unavailable)
        );

        issue(&mut store, "wrong-view", now);
        assert_eq!(
            store.consume("wrong-view", "other", SOURCE, 7, NONCE, 2, now),
            Err(FilePathError::Unavailable)
        );
        issue(&mut store, "wrong-document", now);
        assert_eq!(
            store.consume("wrong-document", "main", SOURCE, 8, NONCE, 2, now),
            Err(FilePathError::Unavailable)
        );
        issue(&mut store, "wrong-nonce", now);
        assert_eq!(
            store.consume(
                "wrong-nonce",
                "main",
                SOURCE,
                7,
                "1123456789abcdef0123456789abcdef",
                2,
                now
            ),
            Err(FilePathError::Unavailable)
        );
        issue(&mut store, "wrong-generation", now);
        assert_eq!(
            store.consume("wrong-generation", "main", SOURCE, 7, NONCE, 3, now),
            Err(FilePathError::Unavailable)
        );

        issue(&mut store, "valid", now);
        assert_eq!(
            store.consume("valid", "main", SOURCE, 7, NONCE, 2, now),
            Ok(())
        );
        assert_eq!(
            store.consume("valid", "main", SOURCE, 7, NONCE, 2, now),
            Err(FilePathError::Unavailable)
        );
    }

    #[test]
    fn issue_rejects_invalid_document_nonces() {
        let mut store = FilePathTickets::default();
        assert_eq!(
            store.issue("main", SOURCE, 7, "short", 2, Instant::now()),
            Err(FilePathError::Unavailable)
        );
        assert!(store.entries.is_empty());
    }

    #[test]
    fn expired_tickets_are_removed() {
        let now = Instant::now();
        let mut store = FilePathTickets::default();
        issue(&mut store, "expired", now);

        assert_eq!(
            store.consume(
                "expired",
                "main",
                SOURCE,
                7,
                NONCE,
                2,
                now + TICKET_LIFETIME
            ),
            Err(FilePathError::Unavailable)
        );
        assert!(store.entries.is_empty());
    }

    #[test]
    fn per_document_and_global_capacity_evicts_oldest_tickets() {
        let now = Instant::now();
        let mut store = FilePathTickets::default();
        for index in 0..MAX_DOCUMENT_TICKETS {
            issue(
                &mut store,
                &format!("doc-{index}"),
                now + Duration::from_millis(index as u64),
            );
        }
        for index in 0..MAX_DOCUMENT_TICKETS {
            store.insert_test_ticket(
                &format!("doc-varied-{index}"),
                "main",
                if index % 2 == 0 {
                    SOURCE
                } else {
                    "https://example.test/other"
                },
                7,
                &format!("{index:032x}"),
                index as u64,
                now + Duration::from_secs(1),
            );
        }
        assert!(!store.entries.contains_key("doc-0"));
        assert_eq!(
            store
                .entries
                .values()
                .filter(|ticket| ticket.webview == "main" && ticket.document_id == 7)
                .count(),
            MAX_DOCUMENT_TICKETS
        );
        assert_eq!(store.entries.len(), MAX_DOCUMENT_TICKETS);

        for index in 0..MAX_TICKETS {
            store.insert_test_ticket(
                &format!("global-{index}"),
                &format!("view-{index}"),
                SOURCE,
                index as u64 + 1,
                NONCE,
                1,
                now + Duration::from_secs(2) + Duration::from_millis(index as u64),
            );
        }
        store.insert_test_ticket(
            "global-newest",
            "view-last",
            SOURCE,
            999,
            NONCE,
            1,
            now + Duration::from_secs(3),
        );
        assert_eq!(store.entries.len(), MAX_TICKETS);
        assert!(!store.entries.contains_key("global-0"));
        assert!(store.entries.contains_key("global-63"));
    }

    #[test]
    fn clearing_reparented_webview_invalidates_preissued_tickets() {
        let now = Instant::now();
        let mut store = FilePathTickets::default();
        issue(&mut store, "first", now);
        store.insert_test_ticket("second", "other", SOURCE, 8, NONCE, 2, now);

        store.clear_webview("main");
        assert!(!store.entries.contains_key("first"));
        assert!(store.entries.contains_key("second"));
        assert_eq!(
            store.consume("first", "main", SOURCE, 7, NONCE, 2, now),
            Err(FilePathError::Unavailable)
        );
    }
}
