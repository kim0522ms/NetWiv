use muda::{
    AboutMetadata,
    CheckMenuItem,
    IconMenuItem,
    IsMenuItem,
    Menu,
    MenuItem,
    MenuItemKind,
    NativeIcon,
    PredefinedMenuItem,
    Submenu,
};
use muda::accelerator::Accelerator;
use serde_json::{Value, json};
use crate::model::rid_arg;
use super::{
    NativeResources,
    MenuResource,
    Resource,
    ChannelTarget,
    creation_channel,
    display,
    invalid_resource,
    string_arg,
    optional_string,
    optional_bool,
    optional_array,
};

#[derive(Clone)]
pub(super) enum MenuNative {
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

    pub(super) fn id(&self) -> &str {
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

    pub(super) fn items(&self) -> Result<Vec<MenuItemKind>, String> {
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

    pub(super) fn insert(&self, item: &dyn IsMenuItem, position: usize) -> Result<(), String> {
        match self {
            Self::Root(value) => value.insert(item, position).map_err(display),
            Self::Submenu(value) => value.insert(item, position).map_err(display),
            _ => Err("menu resource cannot contain child items".into()),
        }
    }

    pub(super) fn remove(&self, item: &dyn IsMenuItem) -> Result<(), String> {
        match self {
            Self::Root(value) => value.remove(item).map_err(display),
            Self::Submenu(value) => value.remove(item).map_err(display),
            _ => Err("menu resource cannot contain child items".into()),
        }
    }

    pub(super) fn remove_at(&self, position: usize) -> Result<Option<MenuNative>, String> {
        match self {
            Self::Root(value) => Ok(value.remove_at(position).map(Self::from_item)),
            Self::Submenu(value) => Ok(value.remove_at(position).map(Self::from_item)),
            _ => Err("unexpected menu item kind".into()),
        }
    }

    pub(super) fn get(&self, id: &str) -> Result<Option<MenuNative>, String> {
        Ok(self
            .items()?
            .into_iter()
            .find(|item| item.id().as_ref() == id)
            .map(Self::from_item))
    }

    pub(super) fn text(&self) -> Result<String, String> {
        match self {
            Self::Submenu(v) => Ok(v.text()),
            Self::Normal(v) => Ok(v.text()),
            Self::Check(v) => Ok(v.text()),
            Self::Icon(v) => Ok(v.text()),
            Self::Predefined(v) => Ok(v.text()),
            _ => Err("menu resource has no mutable text".into()),
        }
    }

    pub(super) fn set_text(&self, text: &str) -> Result<(), String> {
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

    pub(super) fn enabled(&self) -> Result<bool, String> {
        match self {
            Self::Submenu(v) => Ok(v.is_enabled()),
            Self::Normal(v) => Ok(v.is_enabled()),
            Self::Check(v) => Ok(v.is_enabled()),
            Self::Icon(v) => Ok(v.is_enabled()),
            _ => Err("menu resource has no enabled state".into()),
        }
    }

    pub(super) fn set_enabled(&self, enabled: bool) -> Result<(), String> {
        match self {
            Self::Submenu(v) => v.set_enabled(enabled),
            Self::Normal(v) => v.set_enabled(enabled),
            Self::Check(v) => v.set_enabled(enabled),
            Self::Icon(v) => v.set_enabled(enabled),
            _ => return Err("menu resource has no enabled state".into()),
        }
        Ok(())
    }

    pub(super) fn checked(&self) -> Result<bool, String> {
        match self {
            Self::Check(v) => Ok(v.is_checked()),
            _ => Err("menu resource is not a check item".into()),
        }
    }

    pub(super) fn set_checked(&self, checked: bool) -> Result<(), String> {
        match self {
            Self::Check(v) => {
                v.set_checked(checked);
                Ok(())
            }
            _ => Err("menu resource is not a check item".into()),
        }
    }

    pub(super) fn set_accelerator(&self, accelerator: Option<Accelerator>) -> Result<(), String> {
        let result = match self {
            Self::Normal(v) => v.set_accelerator(accelerator),
            Self::Check(v) => v.set_accelerator(accelerator),
            Self::Icon(v) => v.set_accelerator(accelerator),
            _ => return Err("menu resource has no accelerator".into()),
        };
        result.map_err(display)
    }
}

impl NativeResources {
    pub(super) fn new_menu(
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

    pub(super) fn resolve_item(
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

    pub(super) fn add_items(
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

    pub(super) fn remove_item(&mut self, args: &Value) -> Result<Value, String> {
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

    pub(super) fn remove_at(&mut self, args: &Value) -> Result<Value, String> {
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

    pub(super) fn items(&mut self, args: &Value) -> Result<Value, String> {
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

    pub(super) fn get_item(&mut self, args: &Value) -> Result<Value, String> {
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

    pub(super) fn set_accelerator(&self, args: &Value) -> Result<Value, String> {
        let accelerator = parse_accelerator_lossy(args.get("accelerator"))?;
        self.menu_for_kind(args)?
            .native
            .set_accelerator(accelerator)?;
        Ok(Value::Null)
    }

    pub(super) fn set_menu_icon(&self, args: &Value) -> Result<Value, String> {
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

    pub(super) fn create_default(&mut self) -> Result<Value, String> {
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

    pub(super) fn menu(&self, rid: u32) -> Result<&MenuResource, String> {
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
    pub(super) fn menu_for_kind(&self, args: &Value) -> Result<&MenuResource, String> {
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
    fn root_menu(&self, rid: u32) -> Result<&Menu, String> {
        match &self.menu(rid)?.native {
            MenuNative::Root(v) => Ok(v),
            _ => Err("tray/window menu must be a root Menu".into()),
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

    fn menu_icon(&self, value: Option<&Value>) -> Result<Option<muda::Icon>, String> {
        self.image_data(value)?
            .map(|(bytes, width, height)| {
                muda::Icon::from_rgba(bytes, width, height).map_err(display)
            })
            .transpose()
    }
}

#[derive(Clone, Copy)]
pub(super) enum Add {
    Append,
    Prepend,
    Insert,
}

enum MenuIcon {
    None,
    Rgba(muda::Icon),
    Native(NativeIcon),
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

pub(super) fn about_metadata(resources: &NativeResources, value: &Value) -> Result<AboutMetadata, String> {
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
