//! GTK dialogs share Tao's UI thread and event loop. Never call a blocking GTK
//! `run` method or rfd's separate GTK thread from this runtime.

use std::cell::RefCell;
use std::path::{Path, PathBuf};

use gtk::glib::SignalHandlerId;
use gtk::prelude::*;
use gtk::{ButtonsType, DialogFlags, FileChooserAction, FileChooserNative, MessageDialog, MessageType, ResponseType};
use rfd::MessageButtons;
use serde_json::{Value, json};
use tao::platform::unix::WindowExtUnix;
use tao::window::Window;

pub(crate) enum PendingDialog {
    File { dialog: FileChooserNative, response: Option<SignalHandlerId> },
    Message { dialog: MessageDialog, response: Option<SignalHandlerId> },
}

impl Drop for PendingDialog {
    fn drop(&mut self) {
        // Disconnect first: programmatic cancellation/destruction must not turn
        // into a user response, and queued completions are filtered by runtime ID.
        match self {
            Self::File { dialog, response } => {
                if let Some(signal) = response.take() { dialog.disconnect(signal); }
                dialog.hide();
                dialog.destroy();
            }
            Self::Message { dialog, response } => {
                if let Some(signal) = response.take() { dialog.disconnect(signal); }
                dialog.hide();
                // SAFETY: this private widget is owned by the pending request;
                // its signal is disconnected and no widget escapes this module.
                unsafe { dialog.destroy(); }
            }
        }
    }
}

pub(crate) fn begin(
    command: &str,
    args: &Value,
    window: &Window,
    complete: impl FnOnce(Result<Value, String>) + 'static,
) -> Result<PendingDialog, String> {
    let parent = window.gtk_window().upcast_ref::<gtk::Window>();
    match command {
        "plugin:dialog|open" | "plugin:dialog|save" => begin_file(command, args, parent, complete),
        "plugin:dialog|message" => begin_message(args, parent, complete),
        _ => Err(format!("unknown dialog command '{command}'")),
    }
}

struct FileOptions {
    title: String,
    save: bool,
    directory: bool,
    multiple: bool,
    default_directory: Option<PathBuf>,
    default_name: Option<String>,
    filters: Vec<(String, Vec<String>)>,
}

impl FileOptions {
    fn parse(command: &str, args: &Value) -> Result<Self, String> {
        let options = args.get("options").ok_or("missing dialog options")?;
        let save = command == "plugin:dialog|save";
        let directory = !save && options.get("directory").and_then(Value::as_bool).unwrap_or(false);
        let multiple = !save && options.get("multiple").and_then(Value::as_bool).unwrap_or(false);
        let title = options.get("title").and_then(Value::as_str)
            .unwrap_or(if save { "Save File" } else if directory { "Select Folder" } else { "Open File" });
        checked_text(title)?;
        let mut result = Self {
            title: title.to_owned(), save, directory, multiple,
            default_directory: None, default_name: None, filters: Vec::new(),
        };
        if let Some(default) = options.get("defaultPath").and_then(Value::as_str) {
            checked_text(default)?;
            let path = Path::new(default);
            // Keep the existing rfd defaultPath contract: a directory selects
            // the initial folder; an existing or prospective file also supplies
            // a filename. GTK may decline a nonexistent initial directory.
            if path.is_file() || !path.exists() {
                if let Some(parent) = path.parent().filter(|parent| parent.components().next().is_some()) {
                    result.default_directory = Some(parent.to_owned());
                }
                result.default_name = path.file_name().map(|name| name.to_string_lossy().into_owned());
            } else {
                result.default_directory = Some(path.to_owned());
            }
        }
        if let Some(filters) = options.get("filters") {
            for filter in filters.as_array().ok_or("dialog filters must be an array")? {
                let name = filter.get("name").and_then(Value::as_str).ok_or("dialog filter name is missing")?;
                checked_text(name)?;
                let extensions = filter.get("extensions").and_then(Value::as_array)
                    .ok_or("dialog filter extensions are missing")?.iter().map(|extension| {
                        let extension = extension.as_str().ok_or("dialog filter extension must be a string")?;
                        checked_text(extension)?;
                        Ok(extension.to_owned())
                    }).collect::<Result<Vec<_>, String>>()?;
                result.filters.push((name.to_owned(), extensions));
            }
        }
        Ok(result)
    }

    fn action(&self) -> FileChooserAction {
        if self.save { FileChooserAction::Save }
        else if self.directory { FileChooserAction::SelectFolder }
        else { FileChooserAction::Open }
    }

    fn use_current_name(&self, selection_exists: bool) -> bool {
        // rfd uses the name entry for a save basename without a starting folder,
        // even if that basename happens to exist in the process directory.
        self.save && (self.default_directory.is_none() || !selection_exists)
    }
}

fn checked_text(value: &str) -> Result<(), String> {
    if value.contains('\0') { Err("dialog text must not contain a NUL character".into()) }
    else { Ok(()) }
}

fn begin_file(
    command: &str,
    args: &Value,
    parent: &gtk::Window,
    complete: impl FnOnce(Result<Value, String>) + 'static,
) -> Result<PendingDialog, String> {
    let options = FileOptions::parse(command, args)?;
    let dialog = FileChooserNative::new(Some(&options.title), Some(parent), options.action(), None, None);
    dialog.set_modal(true);
    dialog.set_select_multiple(options.multiple);
    dialog.set_do_overwrite_confirmation(options.save);
    for (name, extensions) in &options.filters {
        // The prior rfd GTK implementation ignores filters for folder pickers.
        if options.directory { break; }
        let filter = gtk::FileFilter::new();
        filter.set_name(Some(name));
        for extension in extensions { filter.add_pattern(&format!("*.{extension}")); }
        dialog.add_filter(filter);
    }
    if let Some(directory) = &options.default_directory { dialog.set_current_folder(directory); }
    if let Some(name) = &options.default_name {
        let path = options.default_directory.as_ref().map(|directory| directory.join(name))
            .unwrap_or_else(|| PathBuf::from(name));
        if options.use_current_name(path.exists()) { dialog.set_current_name(name); }
        else { dialog.set_filename(path); }
    }
    let complete = RefCell::new(Some(complete));
    let response = dialog.connect_response(move |dialog, response| {
        let complete = complete.borrow_mut().take();
        if let Some(complete) = complete {
            let value = file_response(response, options.multiple, || dialog.filenames());
            dialog.hide();
            complete(Ok(value));
        }
    });
    dialog.show();
    Ok(PendingDialog::File { dialog, response: Some(response) })
}

fn file_response(response: ResponseType, multiple: bool, paths: impl FnOnce() -> Vec<PathBuf>) -> Value {
    if response != ResponseType::Accept { return Value::Null; }
    let paths = paths().into_iter().map(|path| path.to_string_lossy().into_owned()).collect::<Vec<_>>();
    if multiple { json!(paths) } else { json!(paths.into_iter().next()) }
}

fn begin_message(
    args: &Value,
    parent: &gtk::Window,
    complete: impl FnOnce(Result<Value, String>) + 'static,
) -> Result<PendingDialog, String> {
    let message = args.get("message").and_then(Value::as_str).ok_or("dialog message is missing")?;
    let title = args.get("title").and_then(Value::as_str).unwrap_or("");
    checked_text(message)?;
    checked_text(title)?;
    let level = match args.get("kind").and_then(Value::as_str).unwrap_or("info") {
        "info" => MessageType::Info,
        "warning" => MessageType::Warning,
        "error" => MessageType::Error,
        other => return Err(format!("unknown dialog kind '{other}'")),
    };
    let buttons = super::parse_buttons(args.get("buttons"))?;
    for (label, _) in custom_buttons(&buttons) { checked_text(label)?; }
    let stock = match buttons {
        MessageButtons::Ok => ButtonsType::Ok,
        MessageButtons::OkCancel => ButtonsType::OkCancel,
        MessageButtons::YesNo => ButtonsType::YesNo,
        _ => ButtonsType::None,
    };
    // Title is the primary label and message the plain secondary text, matching
    // the existing GTK presentation. The binding supplies a literal format.
    let dialog = MessageDialog::new(Some(parent), DialogFlags::MODAL, level, stock, title);
    dialog.set_title(title);
    dialog.set_secondary_text(Some(message));
    for (label, response) in custom_buttons(&buttons) { dialog.add_button(label, response); }
    if let Ok(area) = dialog.message_area().downcast::<gtk::Container>() {
        for child in area.children() {
            if let Ok(label) = child.downcast::<gtk::Label>() { label.set_selectable(true); }
        }
    }
    let complete = RefCell::new(Some(complete));
    let response = dialog.connect_response(move |dialog, response| {
        let complete = complete.borrow_mut().take();
        if let Some(complete) = complete {
            let value = message_response(&buttons, response);
            dialog.hide();
            complete(Ok(json!(value)));
        }
    });
    dialog.show_all();
    Ok(PendingDialog::Message { dialog, response: Some(response) })
}

fn custom_buttons(buttons: &MessageButtons) -> Vec<(&str, ResponseType)> {
    match buttons {
        MessageButtons::YesNoCancel => vec![("Yes", ResponseType::Yes), ("No", ResponseType::No), ("Cancel", ResponseType::Cancel)],
        MessageButtons::OkCustom(ok) => vec![(ok, ResponseType::Ok)],
        MessageButtons::OkCancelCustom(ok, cancel) => vec![(ok, ResponseType::Ok), (cancel, ResponseType::Cancel)],
        MessageButtons::YesNoCancelCustom(yes, no, cancel) => vec![(yes, ResponseType::Yes), (no, ResponseType::No), (cancel, ResponseType::Cancel)],
        _ => Vec::new(),
    }
}

fn message_response(buttons: &MessageButtons, response: ResponseType) -> String {
    use MessageButtons::*;
    match (buttons, response) {
        (Ok | OkCancel, ResponseType::Ok) => "Ok",
        (YesNo | YesNoCancel, ResponseType::Yes) => "Yes",
        (YesNo | YesNoCancel, ResponseType::No) => "No",
        (OkCustom(label) | OkCancelCustom(label, _), ResponseType::Ok) => label,
        (OkCancelCustom(_, label) | YesNoCancelCustom(_, _, label), ResponseType::Cancel) => label,
        (YesNoCancelCustom(label, _, _), ResponseType::Yes) => label,
        (YesNoCancelCustom(_, label, _), ResponseType::No) => label,
        _ => "Cancel",
    }.to_owned()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn file_responses_preserve_single_multiple_and_user_cancel_shapes() {
        let paths = || vec![PathBuf::from("sample-a.txt"), PathBuf::from("sample-b.txt")];
        assert_eq!(file_response(ResponseType::Accept, false, paths), json!("sample-a.txt"));
        assert_eq!(file_response(ResponseType::Accept, true, paths), json!(["sample-a.txt", "sample-b.txt"]));
        for response in [ResponseType::Cancel, ResponseType::DeleteEvent, ResponseType::None] {
            assert_eq!(file_response(response, true, || panic!("cancel must not read selection")), Value::Null);
        }
    }

    #[test]
    fn message_responses_preserve_all_custom_labels_and_window_close() {
        let cases = [
            (json!("Ok"), vec![(ResponseType::Ok, "Ok")]),
            (json!("OkCancel"), vec![(ResponseType::Ok, "Ok"), (ResponseType::Cancel, "Cancel")]),
            (json!("YesNo"), vec![(ResponseType::Yes, "Yes"), (ResponseType::No, "No")]),
            (json!("YesNoCancel"), vec![(ResponseType::Yes, "Yes"), (ResponseType::No, "No"), (ResponseType::Cancel, "Cancel")]),
            (json!({"OkCustom":"Proceed"}), vec![(ResponseType::Ok, "Proceed")]),
            (json!({"OkCancelCustom":["Proceed", "Stop"]}), vec![(ResponseType::Ok, "Proceed"), (ResponseType::Cancel, "Stop")]),
            (json!({"YesNoCancelCustom":["Pick", "Leave", "Stop"]}), vec![(ResponseType::Yes, "Pick"), (ResponseType::No, "Leave"), (ResponseType::Cancel, "Stop")]),
        ];
        for (value, responses) in cases {
            let buttons = super::super::parse_buttons(Some(&value)).unwrap();
            for (response, expected) in responses { assert_eq!(message_response(&buttons, response), expected); }
            assert_eq!(message_response(&buttons, ResponseType::DeleteEvent), "Cancel");
        }
    }

    #[test]
    fn file_options_preserve_modes_filters_and_prospective_save_name() {
        for (directory, multiple, action) in [
            (false, false, FileChooserAction::Open), (false, true, FileChooserAction::Open),
            (true, false, FileChooserAction::SelectFolder), (true, true, FileChooserAction::SelectFolder),
        ] {
            let options = FileOptions::parse("plugin:dialog|open", &json!({"options": {"directory":directory,"multiple":multiple}})).unwrap();
            assert_eq!(options.action(), action);
            assert_eq!(options.multiple, multiple);
        }
        let options = FileOptions::parse("plugin:dialog|save", &json!({"options": {
            "defaultPath":"netwiv-dialog-fixture/new-sample.txt", "title":"Synthetic save", "directory":true,"multiple":true,
            "filters":[{"name":"Text", "extensions":["txt", "md"]}]
        }})).unwrap();
        assert_eq!(options.action(), FileChooserAction::Save);
        assert!(!options.multiple);
        assert_eq!(options.default_directory, Some(PathBuf::from("netwiv-dialog-fixture")));
        assert_eq!(options.default_name.as_deref(), Some("new-sample.txt"));
        assert_eq!(options.filters, vec![("Text".into(), vec!["txt".into(), "md".into()])]);
        let directory = FileOptions::parse("plugin:dialog|open", &json!({"options":{"defaultPath":"."}})).unwrap();
        assert_eq!(directory.default_directory, Some(PathBuf::from(".")));
        assert_eq!(directory.default_name, None);
        for options in [
            json!({"filters":false}), json!({"filters":[{}]}),
            json!({"filters":[{"name":"Text","extensions":[true]}]}),
            json!({"title":"bad\0title"}), json!({"defaultPath":"bad\0path"}),
        ] {
            assert!(FileOptions::parse("plugin:dialog|open", &json!({"options":options})).is_err());
        }
    }

    #[test]
    fn invalid_message_buttons_and_nul_text_fail_before_gtk_allocation() {
        for buttons in [json!("Unknown"), json!(false), json!({"OkCustom":false}),
            json!({"OkCancelCustom":["Only"]}), json!({"YesNoCancelCustom":["Yes", "No", false]})] {
            assert!(super::super::parse_buttons(Some(&buttons)).is_err());
        }
        assert!(checked_text("synthetic %s <plain text>").is_ok());
        assert!(checked_text("bad\0text").is_err());
    }

    #[test]
    fn bare_save_name_uses_name_entry_even_when_selection_exists() {
        let mut save = FileOptions::parse("plugin:dialog|save", &json!({"options":{"defaultPath":"sample.txt"}})).unwrap();
        assert!(save.default_directory.is_none());
        assert!(save.use_current_name(true));
        assert!(save.use_current_name(false));
        save.default_directory = Some(PathBuf::from("synthetic-folder"));
        assert!(!save.use_current_name(true));
        assert!(save.use_current_name(false));
        let open = FileOptions::parse("plugin:dialog|open", &json!({"options":{"defaultPath":"sample.txt"}})).unwrap();
        assert!(!open.use_current_name(true));
        assert!(!open.use_current_name(false));
    }
}
