use std::collections::HashMap;

use muda::{
    AboutMetadata, CheckMenuItem, IconMenuItem, IsMenuItem, Menu, MenuItem, MenuItemKind,
    NativeIcon, PredefinedMenuItem, Submenu, accelerator::Accelerator,
};
use serde_json::{Value, json};
use tray_icon::{TrayIcon, TrayIconBuilder};

use crate::model::{bool_arg, rid_arg};

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

#[derive(Clone)]
enum MenuNative {
    Root(Menu),
    Submenu(Submenu),
    Normal(MenuItem),
    Check(CheckMenuItem),
    Icon(IconMenuItem),
    Predefined(PredefinedMenuItem),
}

impl MenuNative {
    fn from_item(item: MenuItemKind) -> Self {
        match item {
            MenuItemKind::MenuItem(value) => Self::Normal(value),
            MenuItemKind::Submenu(value) => Self::Submenu(value),
            MenuItemKind::Predefined(value) => Self::Predefined(value),
            MenuItemKind::Check(value) => Self::Check(value),
            MenuItemKind::Icon(value) => Self::Icon(value),
        }
    }

    fn id(&self) -> &str {
        match self {
            Self::Root(value) => value.id().as_ref(),
            Self::Submenu(value) => value.id().as_ref(),
            Self::Normal(value) => value.id().as_ref(),
            Self::Check(value) => value.id().as_ref(),
            Self::Icon(value) => value.id().as_ref(),
            Self::Predefined(value) => value.id().as_ref(),
        }
    }

    fn kind(&self) -> &'static str {
        match self {
            Self::Root(_) => "Menu",
            Self::Submenu(_) => "Submenu",
            Self::Normal(_) => "MenuItem",
            Self::Check(_) => "Check",
            Self::Icon(_) => "Icon",
            Self::Predefined(_) => "Predefined",
        }
    }

    fn with_item<T>(&self, action: impl FnOnce(&dyn IsMenuItem) -> T) -> Result<T, String> {
        Ok(match self {
            Self::Root(_) => return Err("a root menu cannot be used as a child item".into()),
            Self::Submenu(v) => action(v),
            Self::Normal(v) => action(v),
            Self::Check(v) => action(v),
            Self::Icon(v) => action(v),
            Self::Predefined(v) => action(v),
        })
    }

    fn items(&self) -> Result<Vec<MenuItemKind>, String> {
        match self {
            Self::Root(value) => Ok(value.items()),
            Self::Submenu(value) => Ok(value.items()),
            _ => Err("menu resource cannot contain child items".into()),
        }
    }

    fn append(&self, item: &dyn IsMenuItem) -> Result<(), String> {
        match self {
            Self::Root(value) => value.append(item).map_err(display),
            Self::Submenu(value) => value.append(item).map_err(display),
            _ => Err("menu resource cannot contain child items".into()),
        }
    }

    fn prepend(&self, item: &dyn IsMenuItem) -> Result<(), String> {
        match self {
            Self::Root(value) => value.prepend(item).map_err(display),
            Self::Submenu(value) => value.prepend(item).map_err(display),
            _ => Err("menu resource cannot contain child items".into()),
        }
    }

    fn insert(&self, item: &dyn IsMenuItem, position: usize) -> Result<(), String> {
        match self {
            Self::Root(value) => value.insert(item, position).map_err(display),
            Self::Submenu(value) => value.insert(item, position).map_err(display),
            _ => Err("menu resource cannot contain child items".into()),
        }
    }

    fn remove(&self, item: &dyn IsMenuItem) -> Result<(), String> {
        match self {
            Self::Root(value) => value.remove(item).map_err(display),
            Self::Submenu(value) => value.remove(item).map_err(display),
            _ => Err("menu resource cannot contain child items".into()),
        }
    }

    fn remove_at(&self, position: usize) -> Result<Option<MenuNative>, String> {
        match self {
            Self::Root(value) => Ok(value.remove_at(position).map(Self::from_item)),
            Self::Submenu(value) => Ok(value.remove_at(position).map(Self::from_item)),
            _ => Err("unexpected menu item kind".into()),
        }
    }

    fn get(&self, id: &str) -> Result<Option<MenuNative>, String> {
        Ok(self
            .items()?
            .into_iter()
            .find(|item| item.id().as_ref() == id)
            .map(Self::from_item))
    }

    fn text(&self) -> Result<String, String> {
        match self {
            Self::Submenu(v) => Ok(v.text()),
            Self::Normal(v) => Ok(v.text()),
            Self::Check(v) => Ok(v.text()),
            Self::Icon(v) => Ok(v.text()),
            Self::Predefined(v) => Ok(v.text()),
            _ => Err("menu resource has no mutable text".into()),
        }
    }

    fn set_text(&self, text: &str) -> Result<(), String> {
        match self {
            Self::Submenu(v) => v.set_text(text),
            Self::Normal(v) => v.set_text(text),
            Self::Check(v) => v.set_text(text),
            Self::Icon(v) => v.set_text(text),
            Self::Predefined(v) => v.set_text(text),
            _ => return Err("menu resource has no mutable text".into()),
        }
        Ok(())
    }

    fn enabled(&self) -> Result<bool, String> {
        match self {
            Self::Submenu(v) => Ok(v.is_enabled()),
            Self::Normal(v) => Ok(v.is_enabled()),
            Self::Check(v) => Ok(v.is_enabled()),
            Self::Icon(v) => Ok(v.is_enabled()),
            _ => Err("menu resource has no enabled state".into()),
        }
    }

    fn set_enabled(&self, enabled: bool) -> Result<(), String> {
        match self {
            Self::Submenu(v) => v.set_enabled(enabled),
            Self::Normal(v) => v.set_enabled(enabled),
            Self::Check(v) => v.set_enabled(enabled),
            Self::Icon(v) => v.set_enabled(enabled),
            _ => return Err("menu resource has no enabled state".into()),
        }
        Ok(())
    }

    fn checked(&self) -> Result<bool, String> {
        match self {
            Self::Check(v) => Ok(v.is_checked()),
            _ => Err("menu resource is not a check item".into()),
        }
    }

    fn set_checked(&self, checked: bool) -> Result<(), String> {
        match self {
            Self::Check(v) => {
                v.set_checked(checked);
                Ok(())
            }
            _ => Err("menu resource is not a check item".into()),
        }
    }

    fn set_accelerator(&self, accelerator: Option<Accelerator>) -> Result<(), String> {
        let result = match self {
            Self::Normal(v) => v.set_accelerator(accelerator),
            Self::Check(v) => v.set_accelerator(accelerator),
            Self::Icon(v) => v.set_accelerator(accelerator),
            _ => return Err("menu resource has no accelerator".into()),
        };
        result.map_err(display)
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

    fn new_menu(
        &mut self,
        args: &Value,
        webview: &str,
        document_id: u64,
        is_backend: bool,
    ) -> Result<Value, String> {
        let kind = string_arg(args, "kind")?;
        let options = crate::menu_payload::new_options(args.get("options"))?;
        let channel = creation_channel(args, webview, document_id, is_backend)?;
        let rid = self.create_menu_resource(kind, &options, channel, webview, document_id)?;
        let item = self.menu(rid)?;
        Ok(json!([rid, item.id]))
    }

    fn create_menu_resource(
        &mut self,
        kind: &str,
        options: &Value,
        channel: Option<ChannelTarget>,
        webview: &str,
        document_id: u64,
    ) -> Result<u32, String> {
        let explicit_id = optional_string(options, "id")?;
        let text = optional_string(options, "text")?.unwrap_or_default();
        let enabled = optional_bool(options, "enabled")?.unwrap_or(true);
        let accelerator = parse_accelerator_lossy(options.get("accelerator"))?;
        let native = match kind {
            "Menu" => MenuNative::Root(match explicit_id {
                Some(id) => Menu::with_id(id),
                None => Menu::new(),
            }),
            "Submenu" => {
                let item = match explicit_id {
                    Some(id) => Submenu::with_id(id, &text, enabled),
                    None => Submenu::new(&text, enabled),
                };
                self.apply_submenu_icon(&item, options.get("icon"))?;
                MenuNative::Submenu(item)
            }
            "Check" => {
                let checked = optional_bool(options, "checked")?.unwrap_or(false);
                MenuNative::Check(match explicit_id {
                    Some(id) => CheckMenuItem::with_id(id, &text, enabled, checked, accelerator),
                    None => CheckMenuItem::new(&text, enabled, checked, accelerator),
                })
            }
            "Icon" => MenuNative::Icon(self.create_icon_item(
                explicit_id,
                &text,
                enabled,
                options.get("icon"),
                accelerator,
            )?),
            "Predefined" => MenuNative::Predefined(predefined(self, options)?),
            "MenuItem" => MenuNative::Normal(match explicit_id {
                Some(id) => MenuItem::with_id(id, &text, enabled, accelerator),
                None => MenuItem::new(&text, enabled, accelerator),
            }),
            other => return Err(format!("unsupported menu kind '{other}'")),
        };
        let id = native.id().to_owned();
        let rid = self.next_rid()?;
        self.menu_ids.insert(id.clone(), rid);
        self.resources.insert(
            rid,
            Resource::Menu(MenuResource {
                id,
                kind: kind.to_owned(),
                native,
                channel,
            }),
        );
        if let Some(items) = optional_array(options, "items")? {
            for item in items {
                let child = self.resolve_item(item, webview, document_id)?;
                self.append_child(rid, child, Add::Append, None)?;
            }
        }
        Ok(rid)
    }

    fn resolve_item(
        &mut self,
        value: &Value,
        webview: &str,
        document_id: u64,
    ) -> Result<u32, String> {
        let (kind, normalized) = crate::menu_payload::normalize_item(value)?;
        let value = &normalized;
        if let Some(pair) = value.as_array() {
            let rid = pair
                .first()
                .and_then(Value::as_u64)
                .and_then(|v| u32::try_from(v).ok())
                .ok_or_else(|| "menu resource tuple has no valid id".to_string())?;
            let kind = pair
                .get(1)
                .and_then(Value::as_str)
                .ok_or_else(|| "menu resource tuple has no valid kind".to_string())?;
            self.ensure_kind(rid, kind)?;
            return Ok(rid);
        }
        let channel = ChannelTarget::parse(value, webview, document_id)?;
        self.create_menu_resource(kind, value, channel, webview, document_id)
    }

    fn add_items(
        &mut self,
        args: &Value,
        add: Add,
        webview: &str,
        document_id: u64,
    ) -> Result<Value, String> {
        let parent = rid_arg(args)?;
        self.ensure_parent_kind(parent, string_arg(args, "kind")?)?;
        let command = match add {
            Add::Append => "append",
            Add::Prepend => "prepend",
            Add::Insert => "insert",
        };
        let raw = crate::menu_payload::items(args.get("items").unwrap_or(&Value::Null), command)?;
        let base = args.get("position").and_then(Value::as_u64).unwrap_or(0) as usize;
        for (offset, value) in raw.iter().enumerate() {
            let child = self.resolve_item(value, webview, document_id)?;
            self.append_child(parent, child, add, Some(base + offset))?;
        }
        Ok(Value::Null)
    }

    fn append_child(
        &mut self,
        parent: u32,
        child: u32,
        add: Add,
        position: Option<usize>,
    ) -> Result<(), String> {
        let item = self.menu(child)?.native.clone();
        let parent = self.menu(parent)?;
        item.with_item(|item| match add {
            Add::Append => parent.native.append(item),
            Add::Prepend => parent.native.prepend(item),
            Add::Insert => parent.native.insert(item, position.unwrap_or(0)),
        })??;
        Ok(())
    }

    fn remove_item(&mut self, args: &Value) -> Result<Value, String> {
        let parent = rid_arg(args)?;
        self.ensure_parent_kind(parent, string_arg(args, "kind")?)?;
        let tuple = args
            .get("item")
            .and_then(Value::as_array)
            .ok_or_else(|| "remove requires an item resource tuple".to_string())?;
        let child = tuple
            .first()
            .and_then(Value::as_u64)
            .and_then(|v| u32::try_from(v).ok())
            .ok_or_else(|| "remove requires an item resource tuple".to_string())?;
        let kind = tuple
            .get(1)
            .and_then(Value::as_str)
            .ok_or_else(|| "remove requires an item resource tuple".to_string())?;
        self.ensure_kind(child, kind)?;
        let item = self.menu(child)?.native.clone();
        let parent = self.menu(parent)?;
        item.with_item(|item| parent.native.remove(item))??;
        Ok(Value::Null)
    }

    fn remove_at(&mut self, args: &Value) -> Result<Value, String> {
        let rid = rid_arg(args)?;
        self.ensure_parent_kind(rid, string_arg(args, "kind")?)?;
        let position = args
            .get("position")
            .and_then(Value::as_u64)
            .ok_or_else(|| "position must be an unsigned integer".to_string())?
            as usize;
        let Some(item) = self.menu(rid)?.native.remove_at(position)? else {
            return Ok(Value::Null);
        };
        let child = self.add_menu_alias(item)?;
        let child_resource = self.menu(child)?;
        Ok(json!([child, child_resource.id, child_resource.kind]))
    }

    fn items(&mut self, args: &Value) -> Result<Value, String> {
        let rid = rid_arg(args)?;
        self.ensure_parent_kind(rid, string_arg(args, "kind")?)?;
        let items = self.menu(rid)?.native.items()?;
        let mut result = Vec::with_capacity(items.len());
        for item in items {
            let rid = self.add_menu_alias(MenuNative::from_item(item))?;
            let item = self.menu(rid)?;
            result.push(json!([rid, item.id, item.kind]));
        }
        Ok(Value::Array(result))
    }

    fn get_item(&mut self, args: &Value) -> Result<Value, String> {
        let rid = rid_arg(args)?;
        self.ensure_parent_kind(rid, string_arg(args, "kind")?)?;
        let id = string_arg(args, "id")?;
        let Some(item) = self.menu(rid)?.native.get(id)? else {
            return Ok(Value::Null);
        };
        let rid = self.add_menu_alias(item)?;
        let item = self.menu(rid)?;
        Ok(json!([rid, item.id, item.kind]))
    }

    fn set_accelerator(&self, args: &Value) -> Result<Value, String> {
        let accelerator = parse_accelerator_lossy(args.get("accelerator"))?;
        self.menu_for_kind(args)?
            .native
            .set_accelerator(accelerator)?;
        Ok(Value::Null)
    }

    fn set_menu_icon(&self, args: &Value) -> Result<Value, String> {
        let value = self.menu_icon_value(args.get("icon"), false)?;
        match &self.menu_for_kind(args)?.native {
            MenuNative::Icon(item) => {
                match value {
                    MenuIcon::Rgba(icon) => {
                        item.set_native_icon(None);
                        item.set_icon(Some(icon));
                    }
                    MenuIcon::Native(icon) => {
                        item.set_icon(None);
                        item.set_native_icon(Some(icon));
                    }
                    MenuIcon::None => {
                        item.set_icon(None);
                        item.set_native_icon(None);
                    }
                }
                Ok(Value::Null)
            }
            MenuNative::Submenu(item) => {
                match value {
                    MenuIcon::Rgba(icon) => {
                        item.set_native_icon(None);
                        item.set_icon(Some(icon));
                    }
                    MenuIcon::Native(icon) => {
                        item.set_icon(None);
                        item.set_native_icon(Some(icon));
                    }
                    MenuIcon::None => {
                        item.set_icon(None);
                        item.set_native_icon(None);
                    }
                }
                Ok(Value::Null)
            }
            _ => Err("unexpected menu item kind".into()),
        }
    }

    fn create_default(&mut self) -> Result<Value, String> {
        let edit = Submenu::with_items(
            "Edit",
            true,
            &[
                &PredefinedMenuItem::undo(None),
                &PredefinedMenuItem::redo(None),
                &PredefinedMenuItem::separator(),
                &PredefinedMenuItem::cut(None),
                &PredefinedMenuItem::copy(None),
                &PredefinedMenuItem::paste(None),
                &PredefinedMenuItem::select_all(None),
            ],
        )
        .map_err(display)?;
        let window = Submenu::with_id_and_items(
            "__tauri_window_menu__",
            "Window",
            true,
            &[
                &PredefinedMenuItem::minimize(None),
                &PredefinedMenuItem::maximize(None),
                &PredefinedMenuItem::close_window(None),
            ],
        )
        .map_err(display)?;
        let help = Submenu::with_id_and_items(
            "__tauri_help_menu__",
            "Help",
            true,
            &[&PredefinedMenuItem::about(None, None)],
        )
        .map_err(display)?;
        #[cfg(target_os = "windows")]
        let root = {
            let file = Submenu::with_items(
                "File",
                true,
                &[
                    &PredefinedMenuItem::close_window(None),
                    &PredefinedMenuItem::quit(None),
                ],
            )
            .map_err(display)?;
            Menu::with_items(&[&file, &edit, &window, &help]).map_err(display)?
        };
        #[cfg(target_os = "linux")]
        let root = Menu::with_items(&[&edit, &window, &help]).map_err(display)?;
        #[cfg(target_os = "macos")]
        let root = {
            let app = Submenu::with_items("Application", true, &[
                &PredefinedMenuItem::about(None, None), &PredefinedMenuItem::separator(),
                &PredefinedMenuItem::services(None), &PredefinedMenuItem::separator(),
                &PredefinedMenuItem::hide(None), &PredefinedMenuItem::hide_others(None),
                &PredefinedMenuItem::show_all(None), &PredefinedMenuItem::separator(),
                &PredefinedMenuItem::quit(None),
            ]).map_err(display)?;
            Menu::with_items(&[&app, &edit, &window, &help]).map_err(display)?
        };
        let rid = self.add_menu_resource(MenuNative::Root(root), None)?;
        let item = self.menu(rid)?;
        Ok(json!([rid, item.id]))
    }

    fn new_tray(
        &mut self,
        args: &Value,
        webview: &str,
        document_id: u64,
        is_backend: bool,
    ) -> Result<Value, String> {
        let options = args
            .get("options")
            .filter(|v| v.is_object())
            .ok_or_else(|| "tray options must be an object".to_string())?;
        let channel = creation_channel(args, webview, document_id, is_backend)?;
        let mut builder = match optional_string(options, "id")? {
            Some(id) => TrayIconBuilder::new().with_id(id),
            None => TrayIconBuilder::new(),
        };
        if let Some(icon) = self.tray_icon_value(options.get("icon"))? {
            builder = builder.with_icon(icon);
        }
        if let Some(value) = optional_string(options, "tooltip")? {
            builder = builder.with_tooltip(value);
        }
        if let Some(value) = optional_string(options, "title")? {
            builder = builder.with_title(value);
        }
        if let Some(value) = optional_string(options, "tempDirPath")? {
            builder = builder.with_temp_dir_path(value);
        }
        if let Some(value) = optional_bool(options, "iconAsTemplate")? {
            builder = builder.with_icon_as_template(value);
        }
        if let Some(value) = optional_bool(options, "menuOnLeftClick")? {
            builder = builder.with_menu_on_left_click(value);
        }
        if let Some(value) = optional_bool(options, "showMenuOnLeftClick")? {
            builder = builder.with_menu_on_left_click(value);
        }
        if let Some(menu) = options.get("menu") {
            builder = builder.with_menu(self.tray_menu(menu)?);
        }
        let native = builder.build().map_err(display)?;
        let id = native.id().as_ref().to_owned();
        let rid = self.next_rid()?;
        self.resources.insert(
            rid,
            Resource::Tray(TrayResource {
                id: id.clone(),
                native,
                channel,
            }),
        );
        self.tray_ids.entry(id.clone()).or_default().push(rid);
        Ok(json!([rid, id]))
    }

    fn get_tray(&mut self, args: &Value) -> Result<Value, String> {
        let id = string_arg(args, "id")?;
        let Some(rid) = self.tray_ids.get(id).and_then(|rids| rids.first()).copied() else {
            return Ok(Value::Null);
        };
        Ok(json!(rid))
    }

    fn remove_tray(&mut self, args: &Value) -> Result<Value, String> {
        let id = string_arg(args, "id")?;
        let rid = self
            .tray_ids
            .get_mut(id)
            .and_then(|rids| (!rids.is_empty()).then(|| rids.remove(0)))
            .ok_or_else(|| format!("Can't find a tray associated with this id: {id}"))?;
        if self.tray_ids.get(id).is_some_and(Vec::is_empty) {
            self.tray_ids.remove(id);
        }
        {
            self.resources.remove(&rid);
        }
        Ok(Value::Null)
    }

    fn set_tray_icon(&self, args: &Value) -> Result<Value, String> {
        let icon = self.tray_icon_value(args.get("icon"))?;
        self.tray(rid_arg(args)?)?
            .native
            .set_icon(icon)
            .map_err(display)?;
        Ok(Value::Null)
    }

    fn set_tray_icon_with_template(&self, args: &Value) -> Result<Value, String> {
        let icon = self.tray_icon_value(args.get("icon"))?;
        let as_template = bool_arg(args, "asTemplate")?;
        self.tray(rid_arg(args)?)?
            .native
            .set_icon_with_as_template(icon, as_template)
            .map_err(display)?;
        Ok(Value::Null)
    }

    pub fn set_tray_menu(&self, args: &Value) -> Result<Value, String> {
        let tray = self.tray(rid_arg(args)?)?;
        let native = match args.get("menu") {
            None | Some(Value::Null) => None,
            Some(value) => Some(self.tray_menu(value)?),
        };
        tray.native.set_menu(native);
        Ok(Value::Null)
    }

    pub fn menu_event(&mut self, native_id: &str) -> Option<(Option<ChannelTarget>, String)> {
        let rid = *self.menu_ids.get(native_id)?;
        let item = self.menu_mut(rid).ok()?;
        let id = item.id.clone();
        let target = item.channel.as_mut().map(|target| {
            let result = target.clone();
            target.index += 1;
            result
        });
        Some((target, id))
    }

    pub fn channel_for_tray_event(&mut self, id: &str) -> Option<(ChannelTarget, String)> {
        let rid = *self.tray_ids.get(id)?.last()?;
        let tray = self.tray_mut(rid).ok()?;
        let result = tray.channel.as_mut()?.clone();
        tray.channel.as_mut()?.index += 1;
        Some((result, tray.id.clone()))
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
    fn menu(&self, rid: u32) -> Result<&MenuResource, String> {
        match self.resources.get(&rid) {
            Some(Resource::Menu(v)) => Ok(v),
            _ => Err(invalid_resource(rid)),
        }
    }
    fn menu_mut(&mut self, rid: u32) -> Result<&mut MenuResource, String> {
        match self.resources.get_mut(&rid) {
            Some(Resource::Menu(v)) => Ok(v),
            _ => Err(invalid_resource(rid)),
        }
    }
    fn menu_for_kind(&self, args: &Value) -> Result<&MenuResource, String> {
        let rid = rid_arg(args)?;
        let kind = string_arg(args, "kind")?;
        self.ensure_kind(rid, kind)?;
        self.menu(rid)
    }
    fn ensure_kind(&self, rid: u32, kind: &str) -> Result<(), String> {
        if self.menu(rid)?.kind == kind {
            Ok(())
        } else {
            Err(invalid_resource(rid))
        }
    }
    fn ensure_parent_kind(&self, rid: u32, kind: &str) -> Result<(), String> {
        match kind {
            "Menu" | "Submenu" => self.ensure_kind(rid, kind),
            _ => Err("unexpected menu item kind".into()),
        }
    }
    fn add_menu_resource(
        &mut self,
        native: MenuNative,
        channel: Option<ChannelTarget>,
    ) -> Result<u32, String> {
        let rid = self.next_rid()?;
        let id = native.id().to_owned();
        let kind = native.kind().to_owned();
        self.menu_ids.insert(id.clone(), rid);
        self.resources.insert(
            rid,
            Resource::Menu(MenuResource {
                id,
                kind,
                native,
                channel,
            }),
        );
        Ok(rid)
    }
    fn add_menu_alias(&mut self, native: MenuNative) -> Result<u32, String> {
        let rid = self.next_rid()?;
        self.resources.insert(
            rid,
            Resource::Menu(MenuResource {
                id: native.id().to_owned(),
                kind: native.kind().to_owned(),
                native,
                channel: None,
            }),
        );
        Ok(rid)
    }
    fn tray(&self, rid: u32) -> Result<&TrayResource, String> {
        match self.resources.get(&rid) {
            Some(Resource::Tray(v)) => Ok(v),
            _ => Err(invalid_resource(rid)),
        }
    }
    fn tray_mut(&mut self, rid: u32) -> Result<&mut TrayResource, String> {
        match self.resources.get_mut(&rid) {
            Some(Resource::Tray(v)) => Ok(v),
            _ => Err(invalid_resource(rid)),
        }
    }
    fn image(&self, rid: u32) -> Result<&ImageResource, String> {
        match self.resources.get(&rid) {
            Some(Resource::Image(v)) => Ok(v),
            _ => Err(invalid_resource(rid)),
        }
    }
    fn root_menu(&self, rid: u32) -> Result<&Menu, String> {
        match &self.menu(rid)?.native {
            MenuNative::Root(v) => Ok(v),
            _ => Err("tray/window menu must be a root Menu".into()),
        }
    }

    fn tray_menu(&self, value: &Value) -> Result<Box<dyn tray_icon::menu::ContextMenu>, String> {
        let pair = value
            .as_array()
            .filter(|pair| pair.len() == 2)
            .ok_or_else(|| "tray menu must be a resource tuple".to_string())?;
        let rid = pair[0]
            .as_u64()
            .and_then(|value| u32::try_from(value).ok())
            .ok_or_else(|| "tray menu has no valid resource id".to_string())?;
        let kind = pair[1]
            .as_str()
            .ok_or_else(|| "tray menu has no valid kind".to_string())?;
        match kind {
            "Menu" => match &self.menu(rid)?.native {
                MenuNative::Root(menu) => Ok(Box::new(menu.clone())),
                _ => Err(invalid_resource(rid)),
            },
            "Submenu" => match &self.menu(rid)?.native {
                MenuNative::Submenu(menu) => Ok(Box::new(menu.clone())),
                _ => Err(invalid_resource(rid)),
            },
            _ => Err("unexpected menu item kind".into()),
        }
    }

    fn create_icon_item(
        &self,
        id: Option<String>,
        text: &str,
        enabled: bool,
        value: Option<&Value>,
        accelerator: Option<Accelerator>,
    ) -> Result<IconMenuItem, String> {
        match self.menu_icon_value(value, true)? {
            MenuIcon::Rgba(icon) => Ok(match id {
                Some(id) => IconMenuItem::with_id(id, text, enabled, Some(icon), accelerator),
                None => IconMenuItem::new(text, enabled, Some(icon), accelerator),
            }),
            MenuIcon::Native(icon) => Ok(match id {
                Some(id) => IconMenuItem::with_id_and_native_icon(
                    id,
                    text,
                    enabled,
                    Some(icon),
                    accelerator,
                ),
                None => IconMenuItem::with_native_icon(text, enabled, Some(icon), accelerator),
            }),
            MenuIcon::None => unreachable!("icon menu defaults to a native User icon"),
        }
    }

    fn apply_submenu_icon(&self, item: &Submenu, value: Option<&Value>) -> Result<(), String> {
        match self.menu_icon_value(value, false)? {
            MenuIcon::None => {}
            MenuIcon::Rgba(icon) => item.set_icon(Some(icon)),
            MenuIcon::Native(icon) => item.set_native_icon(Some(icon)),
        }
        Ok(())
    }

    fn menu_icon_value(
        &self,
        value: Option<&Value>,
        default_user: bool,
    ) -> Result<MenuIcon, String> {
        let Some(value) = value else {
            return Ok(if default_user {
                MenuIcon::Native(NativeIcon::User)
            } else {
                MenuIcon::None
            });
        };
        if value.is_null() {
            return Ok(if default_user {
                MenuIcon::Native(NativeIcon::User)
            } else {
                MenuIcon::None
            });
        }
        if value.is_string()
            && let Ok(icon) = serde_json::from_value::<NativeIcon>(value.clone())
        {
            return Ok(MenuIcon::Native(icon));
        }
        self.menu_icon(Some(value))?
            .map(MenuIcon::Rgba)
            .ok_or_else(|| "menu icon is required".into())
    }
    pub fn root_menu_clone(&self, rid: u32) -> Result<Menu, String> {
        self.root_menu(rid).cloned()
    }

    pub fn add_root_menu_alias(&mut self, menu: Menu) -> Result<Value, String> {
        let rid = self.add_menu_alias(MenuNative::Root(menu))?;
        Ok(json!([rid, self.menu(rid)?.id]))
    }

    pub fn popup_menu_clone(&self, args: &Value) -> Result<Box<dyn muda::ContextMenu>, String> {
        let rid = rid_arg(args)?;
        match string_arg(args, "kind")? {
            "Menu" => match &self.menu(rid)?.native {
                MenuNative::Root(menu) => Ok(Box::new(menu.clone())),
                _ => Err(invalid_resource(rid)),
            },
            "Submenu" => match &self.menu(rid)?.native {
                MenuNative::Submenu(menu) => Ok(Box::new(menu.clone())),
                _ => Err(invalid_resource(rid)),
            },
            _ => Err("unexpected menu item kind".into()),
        }
    }

    pub fn window_icon(&self, value: Option<&Value>) -> Result<Option<tao::window::Icon>, String> {
        self.image_data(value)?
            .map(|(bytes, width, height)| {
                tao::window::Icon::from_rgba(bytes, width, height).map_err(display)
            })
            .transpose()
    }

    fn image_from_bytes(&mut self, args: &Value) -> Result<Value, String> {
        let bytes = json_bytes(args.get("bytes").ok_or("image bytes are required")?)?;
        let image = decode_image(&bytes)?;
        let rid = self.next_rid()?;
        self.resources.insert(rid, Resource::Image(image));
        Ok(json!(rid))
    }

    fn image_from_path(&mut self, args: &Value) -> Result<Value, String> {
        let path = string_arg(args, "path")?;
        let bytes = std::fs::read(path).map_err(display)?;
        let image = decode_image(&bytes)?;
        let rid = self.next_rid()?;
        self.resources.insert(rid, Resource::Image(image));
        Ok(json!(rid))
    }

    pub(crate) fn image_data(&self, value: Option<&Value>) -> Result<Option<(Vec<u8>, u32, u32)>, String> {
        let Some(value) = value else {
            return Ok(None);
        };
        if value.is_null() {
            return Ok(None);
        }
        if let Some(rid) = value.as_u64().and_then(|v| u32::try_from(v).ok()) {
            let image = self.image(rid)?;
            return Ok(Some((image.rgba.clone(), image.width, image.height)));
        }
        if let Some(path) = value.as_str() {
            let bytes = std::fs::read(path).map_err(display)?;
            let image = decode_image(&bytes)?;
            return Ok(Some((image.rgba, image.width, image.height)));
        }
        if value.is_array() {
            let image = decode_image(&json_bytes(value)?)?;
            return Ok(Some((image.rgba, image.width, image.height)));
        }
        rgba(Some(value))
    }
    pub(crate) fn insert_image(&mut self, rgba: Vec<u8>, width: u32, height: u32) -> Result<u32, String> {
        let rid = self.next_rid()?;
        self.resources.insert(rid, Resource::Image(ImageResource { rgba, width, height }));
        Ok(rid)
    }
    fn menu_icon(&self, value: Option<&Value>) -> Result<Option<muda::Icon>, String> {
        self.image_data(value)?
            .map(|(bytes, width, height)| {
                muda::Icon::from_rgba(bytes, width, height).map_err(display)
            })
            .transpose()
    }
    fn tray_icon_value(&self, value: Option<&Value>) -> Result<Option<tray_icon::Icon>, String> {
        self.image_data(value)?
            .map(|(bytes, width, height)| {
                tray_icon::Icon::from_rgba(bytes, width, height).map_err(display)
            })
            .transpose()
    }
}

#[derive(Clone, Copy)]
enum Add {
    Append,
    Prepend,
    Insert,
}

enum MenuIcon {
    None,
    Rgba(muda::Icon),
    Native(NativeIcon),
}
fn display(error: impl std::fmt::Display) -> String {
    error.to_string()
}

fn invalid_resource(rid: u32) -> String {
    format!("The resource id {rid} is invalid.")
}

fn parse_accelerator_lossy(value: Option<&Value>) -> Result<Option<Accelerator>, String> {
    match value {
        None | Some(Value::Null) => Ok(None),
        Some(Value::String(value)) => Ok(value.parse().ok()),
        Some(_) => Err("'accelerator' must be a string or null".into()),
    }
}

fn predefined(resources: &NativeResources, options: &Value) -> Result<PredefinedMenuItem, String> {
    let item = options
        .get("item")
        .ok_or_else(|| "predefined menu item requires 'item'".to_string())?;
    let text = options.get("text").and_then(Value::as_str);
    if let Some(metadata) = item.get("About") {
        return Ok(PredefinedMenuItem::about(
            text,
            if metadata.is_null() {
                None
            } else {
                Some(about_metadata(resources, metadata)?)
            },
        ));
    }
    let item = item
        .as_str()
        .ok_or_else(|| "invalid predefined menu item".to_string())?;
    Ok(match item {
        "Separator" => PredefinedMenuItem::separator(),
        "Copy" => PredefinedMenuItem::copy(text),
        "Cut" => PredefinedMenuItem::cut(text),
        "Paste" => PredefinedMenuItem::paste(text),
        "SelectAll" => PredefinedMenuItem::select_all(text),
        "Undo" => PredefinedMenuItem::undo(text),
        "Redo" => PredefinedMenuItem::redo(text),
        "Minimize" => PredefinedMenuItem::minimize(text),
        "Maximize" => PredefinedMenuItem::maximize(text),
        "Fullscreen" => PredefinedMenuItem::fullscreen(text),
        "Hide" => PredefinedMenuItem::hide(text),
        "HideOthers" => PredefinedMenuItem::hide_others(text),
        "ShowAll" => PredefinedMenuItem::show_all(text),
        "CloseWindow" => PredefinedMenuItem::close_window(text),
        "Quit" => PredefinedMenuItem::quit(text),
        "Services" => PredefinedMenuItem::services(text),
        "BringAllToFront" => PredefinedMenuItem::bring_all_to_front(text),
        other => return Err(format!("unsupported predefined menu item '{other}'")),
    })
}

fn about_metadata(resources: &NativeResources, value: &Value) -> Result<AboutMetadata, String> {
    if !value.is_object() {
        return Err("about metadata must be an object".into());
    }
    let authors = match value.get("authors") {
        None | Some(Value::Null) => None,
        Some(Value::Array(values)) => Some(
            values
                .iter()
                .map(|value| {
                    value
                        .as_str()
                        .map(str::to_owned)
                        .ok_or_else(|| "about metadata authors must be strings".to_string())
                })
                .collect::<Result<Vec<_>, _>>()?,
        ),
        Some(_) => return Err("about metadata authors must be an array".into()),
    };
    Ok(AboutMetadata {
        name: optional_string(value, "name")?,
        version: optional_string(value, "version")?,
        short_version: optional_string(value, "shortVersion")?,
        authors,
        comments: optional_string(value, "comments")?,
        copyright: optional_string(value, "copyright")?,
        license: optional_string(value, "license")?,
        website: optional_string(value, "website")?,
        website_label: optional_string(value, "websiteLabel")?,
        credits: optional_string(value, "credits")?,
        icon: resources.menu_icon(value.get("icon"))?,
    })
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

fn rgba(value: Option<&Value>) -> Result<Option<(Vec<u8>, u32, u32)>, String> {
    let Some(value) = value else { return Ok(None) };
    if value.is_null() {
        return Ok(None);
    }
    let width = value
        .get("width")
        .and_then(Value::as_u64)
        .and_then(|v| u32::try_from(v).ok())
        .ok_or_else(|| "icon width must be an unsigned integer".to_string())?;
    let height = value
        .get("height")
        .and_then(Value::as_u64)
        .and_then(|v| u32::try_from(v).ok())
        .ok_or_else(|| "icon height must be an unsigned integer".to_string())?;
    let bytes = value
        .get("rgba")
        .and_then(Value::as_array)
        .ok_or_else(|| "icon rgba must be an array".to_string())?
        .iter()
        .map(|v| {
            v.as_u64()
                .and_then(|n| u8::try_from(n).ok())
                .ok_or_else(|| "icon rgba values must be bytes".to_string())
        })
        .collect::<Result<Vec<_>, _>>()?;
    let expected = usize::try_from(width)
        .ok()
        .and_then(|width| {
            usize::try_from(height)
                .ok()
                .and_then(|height| width.checked_mul(height))
        })
        .and_then(|pixels| pixels.checked_mul(4))
        .ok_or("icon dimensions overflow the native address space")?;
    if bytes.len() != expected {
        return Err(format!(
            "icon rgba length {} does not match {}x{} RGBA dimensions ({expected})",
            bytes.len(),
            width,
            height
        ));
    }
    Ok(Some((bytes, width, height)))
}

fn json_bytes(value: &Value) -> Result<Vec<u8>, String> {
    value
        .as_array()
        .ok_or_else(|| "encoded image bytes must be an array".to_string())?
        .iter()
        .map(|v| {
            v.as_u64()
                .and_then(|n| u8::try_from(n).ok())
                .ok_or_else(|| "encoded image values must be bytes".to_string())
        })
        .collect()
}

fn decode_image(bytes: &[u8]) -> Result<ImageResource, String> {
    // Use the pinned Tauri codec and feature set for pixels, format selection and errors.
    let image = image::load_from_memory(bytes)
        .map_err(|error| format!("failed to process image: {error}"))?
        .into_rgba8();
    Ok(ImageResource {
        width: image.width(),
        height: image.height(),
        rgba: image.into_raw(),
    })
}

#[cfg(test)]
mod tests {
    use super::{NativeResources, about_metadata, creation_channel};
    use serde_json::json;
    #[cfg(not(target_os = "macos"))]
    use serde_json::Value;

    #[test]
    fn encoded_image_fixtures_preserve_pixels_and_dimensions() {
        for (bytes, expected) in [
            (include_bytes!("../../../tests/conformance/frontend/images/rgba.png").as_slice(), vec![255,0,0,255,0,255,0,128]),
            (include_bytes!("../../../tests/conformance/frontend/images/palette.png").as_slice(), vec![255,0,0,255,0,255,0,128]),
            (include_bytes!("../../../tests/conformance/frontend/images/gray16.png").as_slice(), vec![0,0,0,255,128,128,128,255,255,255,255,255]),
            (include_bytes!("../../../tests/conformance/frontend/images/multi.ico").as_slice(), vec![255,0,0,255,0,255,0,128]),
        ] {
            let image = super::decode_image(bytes).unwrap();
            assert_eq!(image.height, 1);
            assert_eq!(image.width as usize, expected.len() / 4);
            assert_eq!(image.rgba, expected);
        }
    }

    #[test]
    fn image_decode_errors_match_tauri_and_do_not_allocate_resources() {
        let mut resources = NativeResources::new();
        for (bytes, expected) in [
            (vec![], "failed to process image: The image format could not be determined"),
            (vec![1,2,3], "failed to process image: The image format could not be determined"),
            (include_bytes!("../../../tests/conformance/frontend/images/rgba.png")[..16].to_vec(), "failed to process image: unexpected end of file"),
        ] {
            assert_eq!(resources.invoke("plugin:image|from_bytes", &json!({"bytes":bytes}), "main", 1).unwrap_err(), expected);
            assert!(resources.resources.is_empty());
        }
        let path = std::env::temp_dir().join(format!("netwiv-missing-image-{}", std::process::id())).join("missing.png");
        let expected = std::fs::read(&path).unwrap_err().to_string();
        assert_eq!(resources.invoke("plugin:image|from_path", &json!({"path":path}), "main", 1).unwrap_err(), expected);
        assert!(resources.resources.is_empty());
    }

    #[test]
    fn invalid_resource_errors_match_tauri() {
        let mut resources = NativeResources::new();
        assert_eq!(
            resources.take_channel(41).unwrap_err(),
            "The resource id 41 is invalid."
        );
    }

    #[test]
    fn image_bytes_round_trip_and_close_invalidates_the_resource() {
        let mut encoded = Vec::new();
        {
            let mut encoder = png::Encoder::new(&mut encoded, 2, 1);
            encoder.set_color(png::ColorType::Rgb);
            encoder.set_depth(png::BitDepth::Eight);
            let mut writer = encoder.write_header().unwrap();
            writer.write_image_data(&[255, 0, 0, 0, 255, 0]).unwrap();
        }

        let mut resources = NativeResources::new();
        let rid = resources
            .invoke(
                "plugin:image|from_bytes",
                &json!({"bytes": encoded}),
                "main",
                1,
            )
            .unwrap()
            .as_u64()
            .unwrap() as u32;
        assert_eq!(
            resources
                .invoke("plugin:image|size", &json!({"rid":rid}), "main", 1)
                .unwrap(),
            json!({"width":2,"height":1})
        );
        assert_eq!(
            resources
                .invoke("plugin:image|rgba", &json!({"rid":rid}), "main", 1)
                .unwrap(),
            json!([255, 0, 0, 255, 0, 255, 0, 255])
        );
        resources
            .invoke("plugin:resources|close", &json!({"rid":rid}), "main", 1)
            .unwrap();
        assert_eq!(
            resources
                .invoke("plugin:image|size", &json!({"rid":rid}), "main", 1)
                .unwrap_err(),
            format!("The resource id {rid} is invalid.")
        );
    }

    #[test]
    fn guest_tray_argument_names_are_enforced_before_resource_lookup() {
        let mut resources = NativeResources::new();
        let invalid = json!({"rid":9,"isTemplate":true});
        assert_eq!(
            resources
                .invoke("plugin:tray|set_icon_as_template", &invalid, "main", 1)
                .unwrap_err(),
            "command argument 'asTemplate' must be a boolean"
        );
        let valid = json!({"rid":9,"asTemplate":true});
        assert_eq!(
            resources
                .invoke("plugin:tray|set_icon_as_template", &valid, "main", 1)
                .unwrap_err(),
            "The resource id 9 is invalid."
        );

        let invalid = json!({"rid":9,"value":false});
        assert_eq!(
            resources
                .invoke(
                    "plugin:tray|set_show_menu_on_left_click",
                    &invalid,
                    "main",
                    1,
                )
                .unwrap_err(),
            "command argument 'onLeft' must be a boolean"
        );
    }

    #[test]
    fn menu_and_tray_new_require_the_channels_sent_by_the_guest_api() {
        let mut resources = NativeResources::new();
        assert!(
            creation_channel(&json!({}), "main", 1, true)
                .unwrap()
                .is_none()
        );
        assert_eq!(
            creation_channel(&json!({}), "main", 1, false).unwrap_err(),
            "'handler' must be a Tauri channel"
        );
        assert_eq!(
            resources
                .invoke(
                    "plugin:menu|new",
                    &json!({"kind":"Menu","options":null}),
                    "main",
                    1,
                )
                .unwrap_err(),
            "'handler' must be a Tauri channel"
        );
        assert_eq!(
            resources
                .invoke("plugin:tray|new", &json!({"options":{}}), "main", 1)
                .unwrap_err(),
            "'handler' must be a Tauri channel"
        );
    }

    #[test]
    fn malformed_nested_menu_payload_is_rejected_before_native_creation() {
        let mut resources = NativeResources::new();
        assert_eq!(
            resources
                .resolve_item(&json!({"checked":true}), "main", 1)
                .unwrap_err(),
            "data did not match any variant of untagged enum MenuItemPayloadKind"
        );
        assert_eq!(resources.resources.len(), 0);
    }

    #[test]
    fn about_metadata_maps_camel_case_fields_and_resolves_image_resources() {
        let mut encoded = Vec::new();
        {
            let mut encoder = png::Encoder::new(&mut encoded, 1, 1);
            encoder.set_color(png::ColorType::Rgba);
            encoder.set_depth(png::BitDepth::Eight);
            let mut writer = encoder.write_header().unwrap();
            writer.write_image_data(&[1, 2, 3, 255]).unwrap();
        }
        let mut resources = NativeResources::new();
        let icon_rid = resources
            .invoke(
                "plugin:image|from_bytes",
                &json!({"bytes":encoded}),
                "main",
                1,
            )
            .unwrap()
            .as_u64()
            .unwrap();

        let metadata = about_metadata(
            &resources,
            &json!({
                "name":"NetWiv",
                "version":"2.11.5",
                "shortVersion":"2.11",
                "authors":["Alice","Bob"],
                "comments":"Desktop runtime",
                "copyright":"Copyright 2026",
                "license":"MIT OR Apache-2.0",
                "website":"https://example.invalid",
                "websiteLabel":"Project site",
                "credits":"Contributors",
                "icon":icon_rid
            }),
        )
        .unwrap();

        assert_eq!(metadata.name.as_deref(), Some("NetWiv"));
        assert_eq!(metadata.version.as_deref(), Some("2.11.5"));
        assert_eq!(metadata.short_version.as_deref(), Some("2.11"));
        assert_eq!(metadata.authors, Some(vec!["Alice".into(), "Bob".into()]));
        assert_eq!(metadata.comments.as_deref(), Some("Desktop runtime"));
        assert_eq!(metadata.copyright.as_deref(), Some("Copyright 2026"));
        assert_eq!(metadata.license.as_deref(), Some("MIT OR Apache-2.0"));
        assert_eq!(metadata.website.as_deref(), Some("https://example.invalid"));
        assert_eq!(metadata.website_label.as_deref(), Some("Project site"));
        assert_eq!(metadata.credits.as_deref(), Some("Contributors"));
        assert!(metadata.icon.is_some());
    }

    #[test]
    fn about_metadata_matches_serde_absent_null_and_wrong_type_behavior() {
        let resources = NativeResources::new();
        let absent = about_metadata(&resources, &json!({})).unwrap();
        assert!(absent.name.is_none());
        assert!(absent.authors.is_none());
        assert!(absent.icon.is_none());

        let null =
            about_metadata(&resources, &json!({"name":null,"authors":null,"icon":null})).unwrap();
        assert!(null.name.is_none());
        assert!(null.authors.is_none());
        assert!(null.icon.is_none());

        assert_eq!(
            about_metadata(&resources, &json!("not an object")).unwrap_err(),
            "about metadata must be an object"
        );
        assert_eq!(
            about_metadata(&resources, &json!({"name":7})).unwrap_err(),
            "'name' must be a string or null"
        );
        assert_eq!(
            about_metadata(&resources, &json!({"authors":"Alice"})).unwrap_err(),
            "about metadata authors must be an array"
        );
        assert_eq!(
            about_metadata(&resources, &json!({"authors":["Alice",7]})).unwrap_err(),
            "about metadata authors must be strings"
        );
        assert_eq!(
            about_metadata(&resources, &json!({"icon":999})).unwrap_err(),
            "The resource id 999 is invalid."
        );
    }

    #[cfg(not(target_os = "macos"))]
    #[test]
    fn nsapp_only_menu_commands_are_noops_on_desktop_targets() {
        let mut resources = NativeResources::new();
        for command in [
            "plugin:menu|set_as_windows_menu_for_nsapp",
            "plugin:menu|set_as_help_menu_for_nsapp",
        ] {
            assert_eq!(
                resources.invoke(command, &json!({"rid":77}), "main", 1),
                Ok(Value::Null)
            );
        }
    }

    #[cfg(target_os = "windows")]
    #[test]
    fn menu_enumeration_returns_independent_handles_and_remove_at_is_nullable() {
        let mut resources = NativeResources::new();
        let menu = resources
            .invoke(
                "plugin:menu|new",
                &json!({
                    "kind":"Menu",
                    "options":{"id":"root"},
                    "handler":"__CHANNEL__:1"
                }),
                "main",
                1,
            )
            .unwrap()[0]
            .as_u64()
            .unwrap() as u32;
        let item = resources
            .invoke(
                "plugin:menu|new",
                &json!({
                    "kind":"MenuItem",
                    "options":{"id":"child","text":"Child"},
                    "handler":"__CHANNEL__:2"
                }),
                "main",
                1,
            )
            .unwrap()[0]
            .as_u64()
            .unwrap() as u32;
        resources
            .invoke(
                "plugin:menu|append",
                &json!({"rid":menu,"kind":"Menu","items":[[item,"MenuItem"]]}),
                "main",
                1,
            )
            .unwrap();
        let first = resources
            .invoke(
                "plugin:menu|items",
                &json!({"rid":menu,"kind":"Menu"}),
                "main",
                1,
            )
            .unwrap();
        let second = resources
            .invoke(
                "plugin:menu|items",
                &json!({"rid":menu,"kind":"Menu"}),
                "main",
                1,
            )
            .unwrap();
        assert_eq!(first[0][1], "child");
        assert_eq!(first[0][2], "MenuItem");
        assert_ne!(first[0][0], second[0][0]);

        let alias = first[0][0].as_u64().unwrap() as u32;
        resources.take_channel(alias).unwrap();
        assert_eq!(
            resources
                .invoke(
                    "plugin:menu|text",
                    &json!({"rid":item,"kind":"MenuItem"}),
                    "main",
                    1,
                )
                .unwrap(),
            "Child"
        );
        let removed = resources
            .invoke(
                "plugin:menu|remove_at",
                &json!({"rid":menu,"kind":"Menu","position":0}),
                "main",
                1,
            )
            .unwrap();
        assert_eq!(removed[1], "child");
        assert_eq!(removed[2], "MenuItem");
        assert_eq!(
            resources
                .invoke(
                    "plugin:menu|remove_at",
                    &json!({"rid":menu,"kind":"Menu","position":0}),
                    "main",
                    1,
                )
                .unwrap(),
            Value::Null
        );
    }
}
