use std::borrow::Cow;

use arboard::{Clipboard, ImageData};
use serde_json::{Value, json};

use crate::resources::NativeResources;

pub(crate) fn invoke(
    command: &str,
    args: &Value,
    owner: &mut Option<Clipboard>,
    resources: &mut NativeResources,
) -> Result<Value, String> {
    let clipboard = match owner {
        Some(clipboard) => clipboard,
        None => owner.insert(Clipboard::new().map_err(|error| error.to_string())?),
    };
    match command {
        "plugin:clipboard-manager|write_text" => {
            let text = string_arg(args, "text")?;
            clipboard
                .set_text(text)
                .map_err(|error| error.to_string())?;
            Ok(Value::Null)
        }
        "plugin:clipboard-manager|read_text" => clipboard
            .get_text()
            .map(|text| json!(text))
            .map_err(|error| error.to_string()),
        "plugin:clipboard-manager|write_html" => {
            let html = string_arg(args, "html")?;
            let alt_text = args.get("altText").and_then(Value::as_str);
            clipboard
                .set_html(html, alt_text)
                .map_err(|error| error.to_string())?;
            Ok(Value::Null)
        }
        "plugin:clipboard-manager|clear" => {
            clipboard.clear().map_err(|error| error.to_string())?;
            Ok(Value::Null)
        }
        "plugin:clipboard-manager|write_image" => {
            let (rgba, width, height) = resources
                .image_data(args.get("image"))?
                .ok_or("clipboard image is required")?;
            clipboard
                .set_image(ImageData {
                    width: width as usize,
                    height: height as usize,
                    bytes: Cow::Owned(rgba),
                })
                .map_err(|error| error.to_string())?;
            Ok(Value::Null)
        }
        "plugin:clipboard-manager|read_image" => {
            let image = clipboard.get_image().map_err(|error| error.to_string())?;
            let width = u32::try_from(image.width).map_err(|_| "clipboard image is too wide")?;
            let height = u32::try_from(image.height).map_err(|_| "clipboard image is too tall")?;
            resources
                .insert_image(image.bytes.into_owned(), width, height)
                .map(|rid| json!(rid))
        }
        _ => Err(format!("unknown clipboard command '{command}'")),
    }
}

fn string_arg<'a>(args: &'a Value, name: &str) -> Result<&'a str, String> {
    args.get(name)
        .and_then(Value::as_str)
        .ok_or_else(|| format!("clipboard argument '{name}' must be a string"))
}
