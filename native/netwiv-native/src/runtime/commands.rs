use std::sync::atomic::Ordering;
use serde_json::{Value, json};
use tao::event_loop::EventLoopWindowTarget;
use crate::model::Submission;
#[cfg(target_os = "macos")]
use crate::model::bool_arg;
use super::{LocalRuntime, UserEvent, InvokeResult, sorted};
use super::options::{parse_window_options, parse_theme};

impl LocalRuntime {
    pub(super) fn invoke(
        &mut self,
        target: &EventLoopWindowTarget<UserEvent>,
        submission: &Submission,
        binary: &[u8],
        request_id: u64,
    ) -> Result<InvokeResult, String> {
        let _ = (&submission.source, &submission.scope, binary, request_id);
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
        if command == "netwiv:evaluate_script" {
            self.begin_script_evaluation(submission, request_id)?;
            return Ok(InvokeResult::Pending);
        }
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
            {
                let shared = self.shared.clone();
                crate::macos::invoke_data_store(command, &submission.args, move |result| {
                    // WKWebView completes asynchronously. Never capture LocalRuntime or
                    // call the managed callback directly: shutdown may precede completion.
                    let _ = shared.enqueue(UserEvent::DataStoreCompletion { request_id, result });
                })?;
                return Ok(InvokeResult::Pending);
            }
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
            #[cfg(target_os = "linux")]
            return self.begin_dialog(submission, request_id);
            #[cfg(not(target_os = "linux"))]
            {
            let label = submission.window_label.as_deref()
                .or_else(|| self.webviews.get(&submission.webview_label).map(|view| view.window_label.as_str()))
                .ok_or("dialog request has no parent window")?;
            let window = self.windows.get(label).ok_or("dialog parent window was not found")?;
            return crate::dialog::invoke(command, &submission.args, &window.window).map(InvokeResult::Json);
            }
        }
        if command.starts_with("plugin:clipboard-manager|") {
            return crate::clipboard::invoke(command, &submission.args, &mut self.clipboard, &mut self.resources)
                .map(InvokeResult::Json);
        }
        if command == "plugin:notification|notify" {
            #[cfg(windows)]
            {
                let shared = self.shared.clone();
                crate::notification::notify(&submission.args, move |result| {
                    let _ = shared.enqueue(UserEvent::NotificationCompletion { request_id, result });
                })?;
                return Ok(InvokeResult::Pending);
            }
            #[cfg(not(windows))]
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
}
