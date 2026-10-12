mod images;
mod menus;
mod trays;
#[cfg(test)]
mod tests;

use std::collections::HashMap;
use serde_json::{Value, json};
use tray_icon::TrayIcon;
use crate::model::{bool_arg, rid_arg};
use self::menus::{Add, MenuNative};

#[derive(Clone, Debug)]
pub(crate) struct ChannelTarget {
    pub webview_label: String,
    pub document_id: u64,
    pub channel_id: u32,
    pub index: u64,
}

impl ChannelTarget {
    pub fn parse(
        value: &Value,
        webview_label: &str,
        document_id: u64,
    ) -> Result<Option<Self>, String> {
        let Some(handler) = value.get("handler") else {
            return Ok(None);
        };
        if handler.is_null() {
            return Ok(None);
        }
        let text = handler
            .as_str()
            .ok_or_else(|| "'handler' must be a Tauri channel".to_string())?;
        let id = text
            .strip_prefix("__CHANNEL__:")
            .and_then(|value| value.parse::<u32>().ok())
            .ok_or_else(|| "'handler' must be a Tauri channel".to_string())?;
        Ok(Some(Self {
            webview_label: webview_label.to_owned(),
            document_id,
            channel_id: id,
            index: 0,
        }))
    }
}

fn creation_channel(
    value: &Value,
    webview: &str,
    document_id: u64,
    is_backend: bool,
) -> Result<Option<ChannelTarget>, String> {
    let channel = ChannelTarget::parse(value, webview, document_id)?;
    if channel.is_none() && !is_backend {
        Err("'handler' must be a Tauri channel".to_string())
    } else {
        Ok(channel)
    }
}

struct MenuResource {
    id: String,
    kind: String,
    native: MenuNative,
    channel: Option<ChannelTarget>,
}

struct TrayResource {
    id: String,
    native: TrayIcon,
    channel: Option<ChannelTarget>,
}

#[derive(Clone)]
struct ImageResource {
    rgba: Vec<u8>,
    width: u32,
    height: u32,
}

enum Resource {
    Menu(MenuResource),
    Tray(TrayResource),
    Image(ImageResource),
}

pub(crate) struct NativeResources {
    next_rid: u32,
    resources: HashMap<u32, Resource>,
    menu_ids: HashMap<String, u32>,
    tray_ids: HashMap<String, Vec<u32>>,
}

impl NativeResources {
    pub fn new() -> Self {
        Self {
            next_rid: 1,
            resources: HashMap::new(),
            menu_ids: HashMap::new(),
            tray_ids: HashMap::new(),
        }
    }

    fn next_rid(&mut self) -> Result<u32, String> {
        let rid = self.next_rid;
        if rid == 0 || rid >= 0x8000_0000 {
            return Err("native resource id space exhausted".into());
        }
        self.next_rid += 1;
        Ok(rid)
    }

    pub fn invoke(
        &mut self,
        command: &str,
        args: &Value,
        webview: &str,
        document_id: u64,
    ) -> Result<Value, String> {
        self.invoke_inner(command, args, webview, document_id, false)
    }

    pub fn invoke_backend(
        &mut self,
        command: &str,
        args: &Value,
        webview: &str,
        document_id: u64,
    ) -> Result<Value, String> {
        self.invoke_inner(command, args, webview, document_id, true)
    }

    fn invoke_inner(
        &mut self,
        command: &str,
        args: &Value,
        webview: &str,
        document_id: u64,
        is_backend: bool,
    ) -> Result<Value, String> {
        match command {
            "plugin:resources|close" => self.close(rid_arg(args)?),
            "plugin:image|from_bytes" => self.image_from_bytes(args),
            "plugin:image|from_path" => self.image_from_path(args),
            "plugin:image|rgba" => Ok(json!(self.image(rid_arg(args)?)?.rgba)),
            "plugin:image|size" => {
                let image = self.image(rid_arg(args)?)?;
                Ok(json!({"width":image.width,"height":image.height}))
            }
            "plugin:menu|new" => self.new_menu(args, webview, document_id, is_backend),
            "plugin:menu|append" => self.add_items(args, Add::Append, webview, document_id),
            "plugin:menu|prepend" => self.add_items(args, Add::Prepend, webview, document_id),
            "plugin:menu|insert" => self.add_items(args, Add::Insert, webview, document_id),
            "plugin:menu|remove" => self.remove_item(args),
            "plugin:menu|remove_at" => self.remove_at(args),
            "plugin:menu|items" => self.items(args),
            "plugin:menu|get" => self.get_item(args),
            "plugin:menu|text" => Ok(json!(self.menu_for_kind(args)?.native.text()?)),
            "plugin:menu|set_text" => {
                let item = self.menu_for_kind(args)?;
                item.native.set_text(string_arg(args, "text")?)?;
                Ok(Value::Null)
            }
            "plugin:menu|is_enabled" => Ok(json!(self.menu_for_kind(args)?.native.enabled()?)),
            "plugin:menu|set_enabled" => {
                self.menu_for_kind(args)?
                    .native
                    .set_enabled(bool_arg(args, "enabled")?)?;
                Ok(Value::Null)
            }
            "plugin:menu|is_checked" => Ok(json!(self.menu(rid_arg(args)?)?.native.checked()?)),
            "plugin:menu|set_checked" => {
                self.menu(rid_arg(args)?)?
                    .native
                    .set_checked(bool_arg(args, "checked")?)?;
                Ok(Value::Null)
            }
            "plugin:menu|set_accelerator" => self.set_accelerator(args),
            "plugin:menu|set_icon" => self.set_menu_icon(args),
            "plugin:menu|set_as_windows_menu_for_nsapp"
            | "plugin:menu|set_as_help_menu_for_nsapp" => {
                #[cfg(target_os = "macos")]
                {
                    let MenuNative::Submenu(menu) = &self.menu(rid_arg(args)?)?.native else { return Err("expected a submenu".into()); };
                    if command == "plugin:menu|set_as_windows_menu_for_nsapp" { menu.set_as_windows_menu_for_nsapp(); }
                    else { menu.set_as_help_menu_for_nsapp(); }
                }
                Ok(Value::Null)
            },
            "plugin:menu|create_default" => self.create_default(),
            "plugin:tray|new" => self.new_tray(args, webview, document_id, is_backend),
            "plugin:tray|get_by_id" => self.get_tray(args),
            "plugin:tray|remove_by_id" => self.remove_tray(args),
            "plugin:tray|set_visible" => {
                self.tray(rid_arg(args)?)?
                    .native
                    .set_visible(bool_arg(args, "visible")?)
                    .map_err(display)?;
                Ok(Value::Null)
            }
            "plugin:tray|set_tooltip" => {
                let value = optional_string(args, "tooltip")?;
                self.tray(rid_arg(args)?)?
                    .native
                    .set_tooltip(value.as_deref())
                    .map_err(display)?;
                Ok(Value::Null)
            }
            "plugin:tray|set_title" => {
                let value = optional_string(args, "title")?;
                self.tray(rid_arg(args)?)?
                    .native
                    .set_title(value.as_deref());
                Ok(Value::Null)
            }
            "plugin:tray|set_icon" => self.set_tray_icon(args),
            "plugin:tray|set_icon_with_as_template" => self.set_tray_icon_with_template(args),
            "plugin:tray|set_icon_as_template" => {
                let as_template = bool_arg(args, "asTemplate")?;
                self.tray(rid_arg(args)?)?
                    .native
                    .set_icon_as_template(as_template);
                Ok(Value::Null)
            }
            "plugin:tray|set_show_menu_on_left_click" => {
                let on_left = bool_arg(args, "onLeft")?;
                self.tray(rid_arg(args)?)?
                    .native
                    .set_show_menu_on_left_click(on_left);
                Ok(Value::Null)
            }
            "plugin:tray|set_temp_dir_path" => {
                let path = optional_string(args, "path")?;
                self.tray(rid_arg(args)?)?
                    .native
                    .set_temp_dir_path(path.as_deref());
                Ok(Value::Null)
            }
            _ => Err(format!(
                "native resource command '{command}' is not implemented"
            )),
        }
    }

    pub fn take_channel(&mut self, rid: u32) -> Result<Option<ChannelTarget>, String> {
        match self
            .resources
            .remove(&rid)
            .ok_or_else(|| invalid_resource(rid))?
        {
            Resource::Menu(mut menu) => {
                self.menu_ids.retain(|_, value| *value != rid);
                Ok(menu.channel.take())
            }
            Resource::Tray(mut tray) => {
                self.tray_ids.retain(|_, values| {
                    values.retain(|value| *value != rid);
                    !values.is_empty()
                });
                Ok(tray.channel.take())
            }
            Resource::Image(_) => Ok(None),
        }
    }

    fn close(&mut self, rid: u32) -> Result<Value, String> {
        self.take_channel(rid)?;
        Ok(Value::Null)
    }
}

fn display(error: impl std::fmt::Display) -> String {
    error.to_string()
}

fn invalid_resource(rid: u32) -> String {
    format!("The resource id {rid} is invalid.")
}

fn string_arg<'a>(value: &'a Value, key: &str) -> Result<&'a str, String> {
    value
        .get(key)
        .and_then(Value::as_str)
        .ok_or_else(|| format!("'{key}' must be a string"))
}

fn optional_string(value: &Value, key: &str) -> Result<Option<String>, String> {
    match value.get(key) {
        None | Some(Value::Null) => Ok(None),
        Some(Value::String(value)) => Ok(Some(value.clone())),
        Some(_) => Err(format!("'{key}' must be a string or null")),
    }
}

fn optional_bool(value: &Value, key: &str) -> Result<Option<bool>, String> {
    match value.get(key) {
        None | Some(Value::Null) => Ok(None),
        Some(Value::Bool(value)) => Ok(Some(*value)),
        Some(_) => Err(format!("'{key}' must be a boolean or null")),
    }
}

fn optional_array<'a>(value: &'a Value, key: &str) -> Result<Option<&'a Vec<Value>>, String> {
    match value.get(key) {
        None | Some(Value::Null) => Ok(None),
        Some(Value::Array(value)) => Ok(Some(value)),
        Some(_) => Err(format!("'{key}' must be an array or null")),
    }
}
