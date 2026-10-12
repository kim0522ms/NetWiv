use serde_json::{Value, json};
use tray_icon::TrayIconBuilder;
use crate::model::{bool_arg, rid_arg};
use super::{
    NativeResources,
    TrayResource,
    Resource,
    ChannelTarget,
    creation_channel,
    display,
    invalid_resource,
    string_arg,
    optional_string,
    optional_bool,
};
use super::menus::MenuNative;

impl NativeResources {
    pub(super) fn new_tray(
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

    pub(super) fn get_tray(&mut self, args: &Value) -> Result<Value, String> {
        let id = string_arg(args, "id")?;
        let Some(rid) = self.tray_ids.get(id).and_then(|rids| rids.first()).copied() else {
            return Ok(Value::Null);
        };
        Ok(json!(rid))
    }

    pub(super) fn remove_tray(&mut self, args: &Value) -> Result<Value, String> {
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

    pub(super) fn set_tray_icon(&self, args: &Value) -> Result<Value, String> {
        let icon = self.tray_icon_value(args.get("icon"))?;
        self.tray(rid_arg(args)?)?
            .native
            .set_icon(icon)
            .map_err(display)?;
        Ok(Value::Null)
    }

    pub(super) fn set_tray_icon_with_template(&self, args: &Value) -> Result<Value, String> {
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

    pub fn channel_for_tray_event(&mut self, id: &str) -> Option<(ChannelTarget, String)> {
        let rid = *self.tray_ids.get(id)?.last()?;
        let tray = self.tray_mut(rid).ok()?;
        let result = tray.channel.as_mut()?.clone();
        tray.channel.as_mut()?.index += 1;
        Some((result, tray.id.clone()))
    }

    pub(super) fn tray(&self, rid: u32) -> Result<&TrayResource, String> {
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

    fn tray_icon_value(&self, value: Option<&Value>) -> Result<Option<tray_icon::Icon>, String> {
        self.image_data(value)?
            .map(|(bytes, width, height)| {
                tray_icon::Icon::from_rgba(bytes, width, height).map_err(display)
            })
            .transpose()
    }
}
