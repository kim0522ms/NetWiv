use serde_json::{Value, json};
use super::{NativeResources, ImageResource, Resource, display, invalid_resource, string_arg};

impl NativeResources {
    pub(super) fn image(&self, rid: u32) -> Result<&ImageResource, String> {
        match self.resources.get(&rid) {
            Some(Resource::Image(v)) => Ok(v),
            _ => Err(invalid_resource(rid)),
        }
    }
    pub fn window_icon(&self, value: Option<&Value>) -> Result<Option<tao::window::Icon>, String> {
        self.image_data(value)?
            .map(|(bytes, width, height)| {
                tao::window::Icon::from_rgba(bytes, width, height).map_err(display)
            })
            .transpose()
    }

    pub(super) fn image_from_bytes(&mut self, args: &Value) -> Result<Value, String> {
        let bytes = json_bytes(args.get("bytes").ok_or("image bytes are required")?)?;
        let image = decode_image(&bytes)?;
        let rid = self.next_rid()?;
        self.resources.insert(rid, Resource::Image(image));
        Ok(json!(rid))
    }

    pub(super) fn image_from_path(&mut self, args: &Value) -> Result<Value, String> {
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

pub(super) fn decode_image(bytes: &[u8]) -> Result<ImageResource, String> {
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
