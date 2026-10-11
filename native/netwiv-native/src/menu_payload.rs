// Wire types adapted from Tauri 2.11.5 menu/plugin.rs and image/mod.rs.
// Copyright 2019-2024 Tauri Programme within The Commons Conservancy.
// SPDX-License-Identifier: Apache-2.0 OR MIT
use muda::NativeIcon;
use serde::{Deserialize, Serialize};
use serde_json::Value;

#[derive(Deserialize, Serialize)]
pub(crate) enum ItemKind {
    Menu,
    MenuItem,
    Predefined,
    Submenu,
    Check,
    Icon,
}

#[derive(Deserialize, Serialize)]
#[serde(untagged)]
enum JsImage {
    Path(String),
    Bytes(Vec<u8>),
    Resource(u32),
    Rgba {
        rgba: Vec<u8>,
        width: u32,
        height: u32,
    },
}

#[derive(Deserialize, Serialize)]
#[serde(untagged)]
enum Icon {
    Native(NativeIcon),
    Icon(JsImage),
}

#[derive(Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
struct AboutMetadata {
    name: Option<String>,
    version: Option<String>,
    short_version: Option<String>,
    authors: Option<Vec<String>>,
    comments: Option<String>,
    copyright: Option<String>,
    license: Option<String>,
    website: Option<String>,
    website_label: Option<String>,
    credits: Option<String>,
    icon: Option<JsImage>,
}

#[allow(clippy::large_enum_variant)]
#[derive(Deserialize, Serialize)]
enum Predefined {
    Separator,
    Copy,
    Cut,
    Paste,
    SelectAll,
    Undo,
    Redo,
    Minimize,
    Maximize,
    Fullscreen,
    Hide,
    HideOthers,
    ShowAll,
    CloseWindow,
    Quit,
    About(Option<AboutMetadata>),
    Services,
    BringAllToFront,
}

#[derive(Serialize)]
struct JavaScriptChannelId(String);

impl<'de> Deserialize<'de> for JavaScriptChannelId {
    fn deserialize<D: serde::Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
        let value = String::deserialize(deserializer)?;
        if value
            .strip_prefix("__CHANNEL__:")
            .and_then(|id| id.parse::<u32>().ok())
            .is_none()
        {
            return Err(serde::de::Error::custom(format!(
                "invalid channel value `{value}`, expected a string in the `__CHANNEL__:ID` format"
            )));
        }
        Ok(Self(value))
    }
}

#[derive(Deserialize, Serialize)]
struct MenuItemPayload {
    handler: Option<JavaScriptChannelId>,
    id: Option<String>,
    text: String,
    enabled: Option<bool>,
    accelerator: Option<String>,
}

#[derive(Deserialize, Serialize)]
struct CheckMenuItemPayload {
    handler: Option<JavaScriptChannelId>,
    id: Option<String>,
    text: String,
    checked: bool,
    enabled: Option<bool>,
    accelerator: Option<String>,
}

#[derive(Deserialize, Serialize)]
struct IconMenuItemPayload {
    handler: Option<JavaScriptChannelId>,
    id: Option<String>,
    text: String,
    icon: Icon,
    enabled: Option<bool>,
    accelerator: Option<String>,
}

#[derive(Deserialize, Serialize)]
struct SubmenuPayload {
    id: Option<String>,
    text: String,
    enabled: Option<bool>,
    items: Vec<MenuItemPayloadKind>,
    icon: Option<Icon>,
}

#[derive(Deserialize, Serialize)]
struct PredefinedMenuItemPayload {
    item: Predefined,
    text: Option<String>,
}

// Untagged variant order is observable: invalid Check fields can fall through
// to MenuItem, and a valid Check takes precedence over Icon/Submenu fields.
#[derive(Deserialize, Serialize)]
#[serde(untagged)]
enum MenuItemPayloadKind {
    ExistingItem((u32, ItemKind)),
    Predefined(PredefinedMenuItemPayload),
    Check(CheckMenuItemPayload),
    Icon(IconMenuItemPayload),
    Submenu(SubmenuPayload),
    MenuItem(MenuItemPayload),
}

#[derive(Deserialize, Serialize, Default)]
#[serde(rename_all = "camelCase")]
pub(crate) struct NewOptions {
    id: Option<String>,
    text: Option<String>,
    enabled: Option<bool>,
    checked: Option<bool>,
    accelerator: Option<String>,
    item: Option<Predefined>,
    icon: Option<Icon>,
    items: Option<Vec<MenuItemPayloadKind>>,
}

impl MenuItemPayloadKind {
    fn normalize(value: &Value) -> Result<(&'static str, Value), String> {
        let item: Self = serde_json::from_value(value.clone()).map_err(|e| e.to_string())?;
        let kind = match &item {
            Self::ExistingItem(_) => "Existing",
            Self::Predefined(_) => "Predefined",
            Self::Check(_) => "Check",
            Self::Icon(_) => "Icon",
            Self::Submenu(_) => "Submenu",
            Self::MenuItem(_) => "MenuItem",
        };
        Ok((kind, serde_json::to_value(item).map_err(|e| e.to_string())?))
    }
}

pub(crate) fn normalize_item(value: &Value) -> Result<(&'static str, Value), String> {
    MenuItemPayloadKind::normalize(value)
}

pub(crate) fn new_options(value: Option<&Value>) -> Result<Value, String> {
    let options: Option<NewOptions> = serde_json::from_value(value.cloned().unwrap_or(Value::Null))
        .map_err(|e| format!("invalid args `options` for command `new`: {e}"))?;
    serde_json::to_value(options.unwrap_or_default()).map_err(|e| e.to_string())
}

pub(crate) fn items(value: &Value, command: &str) -> Result<Vec<Value>, String> {
    let items: Vec<MenuItemPayloadKind> = serde_json::from_value(value.clone())
        .map_err(|e| format!("invalid args `items` for command `{command}`: {e}"))?;
    items
        .into_iter()
        .map(|item| serde_json::to_value(item).map_err(|e| e.to_string()))
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn nested_payload_precedence_and_fallback_match_pinned_serde() {
        for (value, expected) in [
            (
                json!({"text":"x","checked":true,"icon":"Folder","items":[]}),
                "Check",
            ),
            (json!({"text":"x","checked":"invalid"}), "MenuItem"),
            (json!({"text":"x","icon":null}), "MenuItem"),
            (json!({"text":"x","items":null}), "MenuItem"),
            (json!({"text":"x","item":{"About":false}}), "MenuItem"),
            (json!({"text":"x","items":[]}), "Submenu"),
        ] {
            assert_eq!(MenuItemPayloadKind::normalize(&value).unwrap().0, expected);
        }
        assert_eq!(
            MenuItemPayloadKind::normalize(&json!({"checked":true})).unwrap_err(),
            "data did not match any variant of untagged enum MenuItemPayloadKind"
        );
    }

    #[test]
    fn new_options_rejects_entire_nested_tree_before_native_construction() {
        assert!(
            new_options(Some(
                &json!({"items":[{"text":"valid"},{"items":[{"text":42}],"text":42}]})
            ))
            .is_err()
        );
        assert_eq!(
            new_options(Some(&json!({"item":{"About":false}}))).unwrap_err(),
            "invalid args `options` for command `new`: invalid type: boolean `false`, expected struct AboutMetadata"
        );
        assert_eq!(
            new_options(Some(&json!({"item":{"About":[]}}))).unwrap_err(),
            "invalid args `options` for command `new`: invalid length 0, expected struct AboutMetadata with 11 elements"
        );
        assert!(new_options(Some(&json!({"items":[[4,"Icon","extra"]]}))).is_err());
    }
}
