use std::path::{Component, Path, PathBuf};
use serde::Deserialize;
use serde_json::{Value, json};
use tao::dpi::{LogicalSize, LogicalUnit, PhysicalUnit, PixelUnit, Size};
use tao::window::{
    CursorIcon,
    ProgressBarState,
    ProgressState,
    ResizeDirection,
    Theme,
    UserAttentionType,
    WindowSizeConstraints,
};
use wry::{ProxyConfig, ProxyEndpoint, Rect};
use crate::model::{WebviewConfig, WindowConfig, validate_runtime_label};

use tauri_utils::config::Color as TauriColor;
use tauri_utils::config::WindowConfig as TauriWindowConfig;

pub(super) fn parse_window_options(options: &Value, command: &str) -> Result<WindowConfig, String> {
    let parsed = parse_tauri_window_config(options, command)?;
    validate_runtime_label(&parsed.label, "window")?;
    if parsed.width <= 0.0 || parsed.height <= 0.0 {
        return Err("window width and height must be positive".into());
    }
    let extra_config = canonical_window_options(&parsed)?;
    Ok(WindowConfig {
        title: parsed.title,
        url: parsed.url.to_string(),
        label: parsed.label,
        width: parsed.width,
        height: parsed.height,
        extra_config,
        webviews: None,
    })
}

pub(super) fn parse_webview_options(options: &Value, command: &str) -> Result<WebviewConfig, String> {
    let parsed = parse_tauri_window_config(options, command)?;
    validate_runtime_label(&parsed.label, "webview")?;
    if parsed.width <= 0.0 || parsed.height <= 0.0 {
        return Err("webview width and height must be positive".into());
    }
    let extra_config = canonical_window_options(&parsed)?;
    Ok(WebviewConfig {
        label: parsed.label,
        url: parsed.url.to_string(),
        x: parsed.x.unwrap_or(0.0),
        y: parsed.y.unwrap_or(0.0),
        width: parsed.width,
        height: parsed.height,
        extra_config,
    })
}

fn parse_tauri_window_config(options: &Value, command: &str) -> Result<TauriWindowConfig, String> {
    let mut config: TauriWindowConfig = serde_json::from_value(options.clone())
        .map_err(|error| format!("invalid args `options` for command `{command}`: {error}"))?;
    if options.get("title").is_none() {
        config.title = "NetWiv App".into();
    }
    Ok(config)
}

pub(super) fn parse_child_webview_options(options: &Value, is_backend: bool) -> Result<WebviewConfig, String> {
    let mut options = options.clone();
    // auto_resize is a Rust builder option, absent from the guest WindowConfig.
    // Only trusted C# submissions may use the corresponding builder field.
    let auto_resize = if is_backend {
        options.as_object_mut().and_then(|fields| fields.remove("autoResize"))
    } else {
        None
    };
    let mut config = parse_webview_options(&options, "create_webview")?;
    let fields = config.extra_config.as_object_mut().expect("normalized window config");
    if let Some(value) = auto_resize {
        let value: Option<bool> = serde_json::from_value(value)
            .map_err(|error| invalid_argument("create_webview", "options", error))?;
        if let Some(value) = value { fields.insert("autoResize".into(), json!(value)); }
    }
    if !is_backend {
        // The pinned guest's WindowConfig visibility does not apply to its
        // WebviewAttributes. C# retains its explicit child Visible extension.
        fields.remove("visible");
    }
    Ok(config)
}

fn canonical_window_options(config: &TauriWindowConfig) -> Result<Value, String> {
    let mut value = serde_json::to_value(config).map_err(|error| error.to_string())?;
    // Serialization supplies pinned defaults and turns accepted aliases into
    // the camel-case keys used by the native adapter. Absent optional settings
    // must remain absent so creation does not execute their setter paths.
    value.as_object_mut().expect("WindowConfig serializes as an object")
        .retain(|_, value| !value.is_null());
    Ok(value)
}

pub(super) fn invalid_argument(command: &str, argument: &str, error: impl std::fmt::Display) -> String {
    format!("invalid args `{argument}` for command `{command}`: {error}")
}

#[derive(Deserialize)]
enum IpcSize {
    Logical { width: f64, height: f64 },
    Physical { width: u32, height: u32 },
}

pub(super) fn size_value(value: &Value) -> Result<(f64, f64, bool), String> {
    match serde_json::from_value::<IpcSize>(value.clone()).map_err(|error| error.to_string())? {
        IpcSize::Logical { width, height } => Ok((width, height, true)),
        IpcSize::Physical { width, height } => Ok((width.into(), height.into(), false)),
    }
}

#[derive(Deserialize)]
enum IpcPosition {
    Logical { x: f64, y: f64 },
    Physical { x: i32, y: i32 },
}

pub(super) fn position_value(value: &Value) -> Result<(f64, f64, bool), String> {
    match serde_json::from_value::<IpcPosition>(value.clone()).map_err(|error| error.to_string())? {
        IpcPosition::Logical { x, y } => Ok((x, y, true)),
        IpcPosition::Physical { x, y } => Ok((x.into(), y.into(), false)),
    }
}
pub(super) fn size_json(rect: Rect, scale_factor: f64) -> Value {
    let value = rect.size.to_physical::<u32>(scale_factor);
    json!({"width":value.width,"height":value.height})
}
pub(super) fn position_json(rect: Rect, scale_factor: f64) -> Value {
    let value = rect.position.to_physical::<i32>(scale_factor);
    json!({"x":value.x,"y":value.y})
}
pub(super) fn user_attention(value: Option<&Value>) -> Result<Option<UserAttentionType>, String> {
    let Some(value) = value else { return Ok(None) };
    if value.is_null() {
        return Ok(None);
    }
    let kind = value
        .get("type")
        .and_then(Value::as_str)
        .ok_or("user attention value requires type")?;
    match kind.to_ascii_lowercase().as_str() {
        "critical" => Ok(Some(UserAttentionType::Critical)),
        "informational" => Ok(Some(UserAttentionType::Informational)),
        _ => Err(format!("unsupported user attention type '{kind}'")),
    }
}

pub(super) fn optional_size(value: Option<&Value>) -> Result<Option<Size>, String> {
    let Some(value) = value else { return Ok(None) };
    if value.is_null() {
        return Ok(None);
    }
    let (width, height, logical) = size_value(value)?;
    Ok(Some(if logical {
        LogicalSize::new(width, height).into()
    } else {
        tao::dpi::PhysicalSize::new(width as u32, height as u32).into()
    }))
}

pub(super) fn optional_string<'a>(value: &'a Value, name: &str) -> Result<Option<&'a str>, String> {
    match value.get(name) {
        None | Some(Value::Null) => Ok(None),
        Some(value) => value
            .as_str()
            .map(Some)
            .ok_or_else(|| format!("window option '{name}' must be a string or null")),
    }
}

pub(super) fn optional_bool(value: &Value, name: &str) -> Result<Option<bool>, String> {
    match value.get(name) {
        None | Some(Value::Null) => Ok(None),
        Some(Value::Bool(value)) => Ok(Some(*value)),
        Some(_) => Err(format!("window option '{name}' must be a boolean or null")),
    }
}

pub(super) fn prevent_overflow_margin(
    value: Option<&Value>,
) -> Result<Option<tao::dpi::PhysicalSize<u32>>, String> {
    match value {
        None | Some(Value::Null) | Some(Value::Bool(false)) => Ok(None),
        Some(Value::Bool(true)) => Ok(Some(tao::dpi::PhysicalSize::new(0, 0))),
        Some(Value::Object(value)) => {
            let dimension = |name: &str| {
                value
                    .get(name)
                    .and_then(Value::as_u64)
                    .and_then(|value| u32::try_from(value).ok())
                    .ok_or_else(|| {
                        format!("preventOverflow margin '{name}' must be an unsigned integer")
                    })
            };
            Ok(Some(tao::dpi::PhysicalSize::new(
                dimension("width")?,
                dimension("height")?,
            )))
        }
        Some(_) => Err("preventOverflow must be a boolean or physical size".into()),
    }
}

pub(super) fn constrained_inner_size(
    inner: tao::dpi::PhysicalSize<u32>,
    outer: tao::dpi::PhysicalSize<u32>,
    work_area: tao::dpi::PhysicalSize<u32>,
    margin: tao::dpi::PhysicalSize<u32>,
) -> tao::dpi::PhysicalSize<u32> {
    let constraint = tao::dpi::PhysicalSize::new(
        work_area.width.saturating_sub(margin.width),
        work_area.height.saturating_sub(margin.height),
    );
    tao::dpi::PhysicalSize::new(
        inner
            .width
            .saturating_sub(outer.width.saturating_sub(constraint.width)),
        inner
            .height
            .saturating_sub(outer.height.saturating_sub(constraint.height)),
    )
}

pub(super) fn resolve_webview_data_directory(
    local_data_directory: Option<&Path>,
    default_webview_data_directory: Option<&Path>,
    label: &str,
    value: Option<&Value>,
) -> Result<Option<PathBuf>, String> {
    let Some(value) = value.filter(|value| !value.is_null()) else {
        return Ok(if cfg!(target_os = "windows") {
            default_webview_data_directory.map(Path::to_path_buf)
        } else {
            None
        });
    };
    let raw = PathBuf::from(
        value
            .as_str()
            .ok_or("webview option 'dataDirectory' must be a path string or null")?,
    );
    let safe = !raw
        .components()
        .any(|component| matches!(component, Component::ParentDir));
    if safe
        && raw.is_relative()
        && let Some(root) = local_data_directory
            .map(Path::to_path_buf)
            .or_else(dirs::data_local_dir)
    {
        return Ok(Some(root.join(label).join(raw)));
    }
    // This preserves pinned `WebviewBuilder::from_config`: validation failure logs that
    // the option is ignored, but the original value remains in the cloned config and is
    // forwarded to the runtime unchanged.
    Ok(Some(raw))
}

pub(super) fn parse_proxy_url(value: Option<&Value>) -> Result<Option<ProxyConfig>, String> {
    let Some(value) = value else { return Ok(None) };
    if value.is_null() {
        return Ok(None);
    }
    let raw = value
        .as_str()
        .ok_or("webview option 'proxyUrl' must be a URL string or null")?;
    let url = url::Url::parse(raw).map_err(|error| format!("invalid proxy URL: {error}"))?;
    let endpoint = ProxyEndpoint {
        host: url.host().map(|host| host.to_string()).unwrap_or_default(),
        port: url.port().map(|port| port.to_string()).unwrap_or_default(),
    };
    match url.scheme() {
        "http" => Ok(Some(ProxyConfig::Http(endpoint))),
        "socks5" => Ok(Some(ProxyConfig::Socks5(endpoint))),
        _ => Err("proxy URL must use http:// or socks5://".into()),
    }
}

pub(super) fn size_constraints(value: &Value) -> Result<WindowSizeConstraints, String> {
    Ok(WindowSizeConstraints {
        min_width: pixel_unit(value.get("minWidth"))?,
        min_height: pixel_unit(value.get("minHeight"))?,
        max_width: pixel_unit(value.get("maxWidth"))?,
        max_height: pixel_unit(value.get("maxHeight"))?,
    })
}

pub(super) fn logical_window_constraints(value: &Value) -> Result<WindowSizeConstraints, String> {
    Ok(WindowSizeConstraints {
        min_width: logical_constraint(value.get("minWidth"), "minWidth")?,
        min_height: logical_constraint(value.get("minHeight"), "minHeight")?,
        max_width: logical_constraint(value.get("maxWidth"), "maxWidth")?,
        max_height: logical_constraint(value.get("maxHeight"), "maxHeight")?,
    })
}

fn logical_constraint(value: Option<&Value>, name: &str) -> Result<Option<PixelUnit>, String> {
    let Some(value) = value else { return Ok(None) };
    let value = value
        .as_f64()
        .filter(|value| value.is_finite() && *value > 0.0)
        .ok_or_else(|| format!("window option '{name}' must be a positive finite number"))?;
    Ok(Some(LogicalUnit::new(value).into()))
}

fn pixel_unit(value: Option<&Value>) -> Result<Option<PixelUnit>, String> {
    let Some(value) = value else { return Ok(None) };
    if value.is_null() {
        return Ok(None);
    }
    if let Some(value) = value.get("Logical").and_then(Value::as_f64) {
        return Ok(Some(LogicalUnit::new(value).into()));
    }
    if let Some(value) = value.get("Physical").and_then(Value::as_i64) {
        let value = i32::try_from(value).map_err(|_| "physical constraint is out of range")?;
        return Ok(Some(PhysicalUnit::new(value).into()));
    }
    Err("size constraint must be Logical, Physical, or null".into())
}

pub(super) fn parse_theme(value: Option<&Value>) -> Result<Option<Theme>, String> {
    match value.and_then(Value::as_str) {
        None => Ok(None),
        Some("light") => Ok(Some(Theme::Light)),
        Some("dark") => Ok(Some(Theme::Dark)),
        Some(other) => Err(format!("unsupported theme '{other}'")),
    }
}

pub(super) fn theme_name(theme: Theme) -> &'static str {
    match theme {
        Theme::Light => "light",
        Theme::Dark => "dark",
        _ => "light",
    }
}

pub(super) fn cursor_icon(value: &str) -> Result<CursorIcon, String> {
    Ok(match value {
        "default" => CursorIcon::Default,
        "crosshair" => CursorIcon::Crosshair,
        "hand" | "pointer" => CursorIcon::Hand,
        "arrow" => CursorIcon::Arrow,
        "move" => CursorIcon::Move,
        "text" => CursorIcon::Text,
        "wait" => CursorIcon::Wait,
        "help" => CursorIcon::Help,
        "progress" => CursorIcon::Progress,
        "notAllowed" | "not-allowed" => CursorIcon::NotAllowed,
        "contextMenu" | "context-menu" => CursorIcon::ContextMenu,
        "cell" => CursorIcon::Cell,
        "verticalText" | "vertical-text" => CursorIcon::VerticalText,
        "alias" => CursorIcon::Alias,
        "copy" => CursorIcon::Copy,
        "noDrop" | "no-drop" => CursorIcon::NoDrop,
        "grab" => CursorIcon::Grab,
        "grabbing" => CursorIcon::Grabbing,
        "allScroll" | "all-scroll" => CursorIcon::AllScroll,
        "zoomIn" | "zoom-in" => CursorIcon::ZoomIn,
        "zoomOut" | "zoom-out" => CursorIcon::ZoomOut,
        "eResize" | "e-resize" => CursorIcon::EResize,
        "nResize" | "n-resize" => CursorIcon::NResize,
        "neResize" | "ne-resize" => CursorIcon::NeResize,
        "nwResize" | "nw-resize" => CursorIcon::NwResize,
        "sResize" | "s-resize" => CursorIcon::SResize,
        "seResize" | "se-resize" => CursorIcon::SeResize,
        "swResize" | "sw-resize" => CursorIcon::SwResize,
        "wResize" | "w-resize" => CursorIcon::WResize,
        "ewResize" | "ew-resize" => CursorIcon::EwResize,
        "nsResize" | "ns-resize" => CursorIcon::NsResize,
        "neswResize" | "nesw-resize" => CursorIcon::NeswResize,
        "nwseResize" | "nwse-resize" => CursorIcon::NwseResize,
        "colResize" | "col-resize" => CursorIcon::ColResize,
        "rowResize" | "row-resize" => CursorIcon::RowResize,
        other => return Err(format!("unsupported cursor icon '{other}'")),
    })
}

pub(super) fn resize_direction(value: &str) -> Result<ResizeDirection, String> {
    Ok(match value.to_ascii_lowercase().as_str() {
        "east" => ResizeDirection::East,
        "north" => ResizeDirection::North,
        "northeast" => ResizeDirection::NorthEast,
        "northwest" => ResizeDirection::NorthWest,
        "south" => ResizeDirection::South,
        "southeast" => ResizeDirection::SouthEast,
        "southwest" => ResizeDirection::SouthWest,
        "west" => ResizeDirection::West,
        _ => return Err(format!("unsupported resize direction '{value}'")),
    })
}

fn color_arg(value: &Value, name: &str) -> Result<(u8, u8, u8, u8), String> {
    let value = value
        .get(name)
        .ok_or_else(|| format!("command argument '{name}' is required"))?;
    parse_color(value)
}

pub(super) fn parse_color(value: &Value) -> Result<(u8, u8, u8, u8), String> {
    serde_json::from_value::<TauriColor>(value.clone())
        .map(Into::into)
        .map_err(|error| error.to_string())
}
pub(super) fn optional_color_arg(value: &Value, name: &str) -> Result<Option<(u8, u8, u8, u8)>, String> {
    match value.get(name) {
        None | Some(Value::Null) => Ok(None),
        Some(_) => color_arg(value, name).map(Some),
    }
}

#[derive(Deserialize)]
#[serde(rename_all = "lowercase")]
enum IpcProgressState {
    None,
    Normal,
    Indeterminate,
    Paused,
    Error,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct IpcProgressBarState {
    status: Option<IpcProgressState>,
    progress: Option<u64>,
    desktop_filename: Option<String>,
}

pub(super) fn progress_state(value: &Value) -> Result<ProgressBarState, String> {
    let value: IpcProgressBarState =
        serde_json::from_value(value.clone()).map_err(|error| error.to_string())?;
    let state = value.status.map(|state| match state {
        IpcProgressState::None => ProgressState::None,
        IpcProgressState::Normal => ProgressState::Normal,
        IpcProgressState::Indeterminate => ProgressState::Indeterminate,
        IpcProgressState::Paused => ProgressState::Paused,
        IpcProgressState::Error => ProgressState::Error,
    });
    Ok(ProgressBarState {
        state,
        progress: value.progress,
        desktop_filename: value.desktop_filename,
    })
}
