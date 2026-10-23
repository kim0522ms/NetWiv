//! AppKit operations kept behind the shared native runtime contract.
use serde_json::Value;
use tao::{
    dpi::{LogicalPosition, LogicalSize, PhysicalPosition, PhysicalSize},
    monitor::MonitorHandle,
    platform::macos::{MonitorHandleExtMacOS, WindowBuilderExtMacOS, WindowExtMacOS},
    window::{Window, WindowBuilder},
};
use tauri_utils::{TitleBarStyle, WindowEffect, WindowEffectState, config::WindowEffectsConfig};
use window_vibrancy::{NSVisualEffectMaterial as Material, NSVisualEffectState as State};

pub(crate) fn set_application_icon(rgba: &[u8], width: u32, height: u32) -> Result<(), String> {
    use image::ImageEncoder;
    use objc2::{AnyThread, MainThreadMarker};
    use objc2_app_kit::{NSApplication, NSImage};
    use objc2_foundation::NSData;

    let main = MainThreadMarker::new().ok_or("Application icons require the main thread.")?;
    let mut png = Vec::new();
    image::codecs::png::PngEncoder::new(&mut png)
        .write_image(rgba, width, height, image::ExtendedColorType::Rgba8)
        .map_err(|error| error.to_string())?;
    let image = NSImage::initWithData(NSImage::alloc(), &NSData::from_vec(png))
        .ok_or("AppKit could not decode the application icon.")?;
    // A window icon is ignored by Tao on macOS; the Dock uses the process icon.
    unsafe { NSApplication::sharedApplication(main).setApplicationIconImage(Some(&image)); }
    Ok(())
}

pub(crate) fn invoke_data_store(
    command: &str,
    args: &Value,
    complete: impl FnOnce(Result<Value, String>) + Send + 'static,
) -> Result<(), String> {
    use objc2::{msg_send, runtime::AnyClass, sel};
    use wry::{WebView, WebViewExtDarwin};

    let identifier = if command == "plugin:app|remove_data_store" {
        Some(serde_json::from_value::<[u8; 16]>(args.get("uuid").cloned().unwrap_or(Value::Null))
            .map_err(|error| format!("invalid data store identifier: {error}"))?)
    } else { None };
    if objc2::MainThreadMarker::new().is_none() {
        return Err("WKWebsiteDataStore operations must run on the process main thread.".into());
    }
    let class = AnyClass::get(c"WKWebsiteDataStore").ok_or("WKWebsiteDataStore is unavailable.")?;
    // Wry's pinned methods do not perform availability checks. Both selectors
    // were introduced in macOS 14; asking NSObject is safe on the macOS 12 baseline.
    let selector = if identifier.is_some() {
        sel!(removeDataStoreForIdentifier:completionHandler:)
    } else { sel!(fetchAllDataStoreIdentifiers:) };
    let available: bool = unsafe { msg_send![class, respondsToSelector: selector] };
    if !available {
        return Err("WKWebsiteDataStore identifier management requires macOS 14 or later.".into());
    }
    match identifier {
        Some(identifier) => WebView::remove_data_store(&identifier, move |result| {
            complete(result.map(|_| Value::Null).map_err(|error| error.to_string()));
        }),
        None => WebView::fetch_data_store_identifiers(move |identifiers| {
            complete(Ok(serde_json::json!(identifiers)));
        }).map_err(|error| error.to_string())?,
    }
    Ok(())
}

pub(crate) fn configure_window(
    mut builder: WindowBuilder,
    extra: &Value,
) -> Result<WindowBuilder, String> {
    let style: TitleBarStyle = serde_json::from_value(
        extra
            .get("titleBarStyle")
            .cloned()
            .unwrap_or(Value::String("Visible".into())),
    )
    .map_err(|e| e.to_string())?;
    builder = builder
        .with_titlebar_transparent(style != TitleBarStyle::Visible)
        .with_fullsize_content_view(style == TitleBarStyle::Overlay)
        .with_title_hidden(
            serde_json::from_value::<Option<bool>>(
                extra.get("hiddenTitle").cloned().unwrap_or(Value::Null),
            )
            .map_err(|e| e.to_string())?
            .unwrap_or(false),
        )
        .with_automatic_window_tabbing(false);
    if let Some(identifier) = serde_json::from_value::<Option<String>>(
        extra
            .get("tabbingIdentifier")
            .cloned()
            .unwrap_or(Value::Null),
    )
    .map_err(|e| e.to_string())?
    {
        builder = builder
            .with_automatic_window_tabbing(true)
            .with_tabbing_identifier(&identifier);
    }
    if let Some(position) = extra
        .get("trafficLightPosition")
        .filter(|value| !value.is_null())
    {
        let x = position["x"]
            .as_f64()
            .ok_or("trafficLightPosition requires x")?;
        let y = position["y"]
            .as_f64()
            .ok_or("trafficLightPosition requires y")?;
        builder = builder.with_traffic_light_inset(LogicalPosition::new(x, y));
    }
    Ok(builder)
}

pub(crate) fn set_title_bar_style(window: &Window, value: &Value) -> Result<(), String> {
    let style: TitleBarStyle = serde_json::from_value(value.clone()).map_err(|e| e.to_string())?;
    window.set_titlebar_transparent(style != TitleBarStyle::Visible);
    window.set_fullsize_content_view(style == TitleBarStyle::Overlay);
    Ok(())
}

pub(crate) fn monitor_work_area(
    monitor: &MonitorHandle,
) -> (PhysicalPosition<i32>, PhysicalSize<u32>) {
    let Some(screen) = monitor.ns_screen() else {
        return (monitor.position(), monitor.size());
    };
    // SAFETY: Tao supplies a live NSScreen and this function runs on the AppKit thread.
    let screen = unsafe { &*screen.cast::<objc2_app_kit::NSScreen>() };
    let frame = screen.frame();
    let visible = screen.visibleFrame();
    // Cocoa is bottom-left based; Tao uses a top-left physical desktop origin.
    let scale = monitor.scale_factor();
    let offset: PhysicalPosition<i32> = LogicalPosition::new(
        visible.origin.x - frame.origin.x,
        frame.size.height - (visible.origin.y - frame.origin.y) - visible.size.height,
    )
    .to_physical(scale);
    let origin = monitor.position();
    (
        PhysicalPosition::new(origin.x + offset.x, origin.y + offset.y),
        LogicalSize::new(visible.size.width, visible.size.height).to_physical(scale),
    )
}

#[allow(deprecated)]
fn material(effect: WindowEffect) -> Option<Material> {
    Some(match effect {
        WindowEffect::AppearanceBased => Material::AppearanceBased,
        WindowEffect::Light => Material::Light,
        WindowEffect::Dark => Material::Dark,
        WindowEffect::MediumLight => Material::MediumLight,
        WindowEffect::UltraDark => Material::UltraDark,
        WindowEffect::Titlebar => Material::Titlebar,
        WindowEffect::Selection => Material::Selection,
        WindowEffect::Menu => Material::Menu,
        WindowEffect::Popover => Material::Popover,
        WindowEffect::Sidebar => Material::Sidebar,
        WindowEffect::HeaderView => Material::HeaderView,
        WindowEffect::Sheet => Material::Sheet,
        WindowEffect::WindowBackground => Material::WindowBackground,
        WindowEffect::HudWindow => Material::HudWindow,
        WindowEffect::FullScreenUI => Material::FullScreenUI,
        WindowEffect::Tooltip => Material::Tooltip,
        WindowEffect::ContentBackground => Material::ContentBackground,
        WindowEffect::UnderWindowBackground => Material::UnderWindowBackground,
        WindowEffect::UnderPageBackground => Material::UnderPageBackground,
        _ => return None,
    })
}

pub(crate) fn set_effects(
    window: &Window,
    config: Option<WindowEffectsConfig>,
) -> Result<(), String> {
    let Some(config) = config else {
        return window_vibrancy::clear_vibrancy(window)
            .map(|_| ())
            .map_err(|e| e.to_string());
    };
    if let Some(effect) = config.effects.into_iter().find_map(material) {
        // apply_vibrancy adds a new view; it does not replace the previous one.
        // Keep one effect per window so a later clear removes the entire effect.
        window_vibrancy::clear_vibrancy(window).map_err(|e| e.to_string())?;
        let state = config.state.map(|state| match state {
            WindowEffectState::FollowsWindowActiveState => State::FollowsWindowActiveState,
            WindowEffectState::Active => State::Active,
            WindowEffectState::Inactive => State::Inactive,
        });
        window_vibrancy::apply_vibrancy(window, effect, state, config.radius)
            .map_err(|e| e.to_string())?;
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn data_store_identifier_validation_precedes_appkit_access() {
        for uuid in [Value::Null, json!([]), json!(vec![0; 15]), json!(vec![0; 17]),
            json!(vec![256; 16]), json!(vec![-1; 16]), json!(vec![1.5; 16]), json!(vec!["0"; 16])] {
            let error = invoke_data_store("plugin:app|remove_data_store", &json!({"uuid":uuid}), |_| panic!("invalid input must not complete asynchronously")).unwrap_err();
            assert!(error.starts_with("invalid data store identifier:"), "{error}");
        }
    }

    #[test]
    fn data_store_operations_reject_workers_without_entering_webkit() {
        for (command, args) in [
            ("plugin:app|fetch_data_store_identifiers", json!({})),
            ("plugin:app|remove_data_store", json!({"uuid": vec![255; 16]})),
        ] {
            assert!(invoke_data_store(command, &args, |_| panic!("worker must not start WebKit")).unwrap_err().contains("process main thread"));
        }
    }

    #[test]
    fn event_loop_rejects_worker_threads_without_entering_appkit() {
        unsafe extern "C" fn callback(
            _: *mut std::ffi::c_void,
            _: u32,
            _: u64,
            _: *const u8,
            _: usize,
            _: *const u8,
            _: usize,
        ) -> i32 {
            panic!("no callbacks expected");
        }
        let shared =
            std::sync::Arc::new(crate::runtime::Shared::new(callback, std::ptr::null_mut()));
        let config =
            serde_json::from_value(json!({"initializationScript":"", "windows":[]})).unwrap();
        assert!(
            crate::runtime::run(shared, config)
                .unwrap_err()
                .contains("process main thread")
        );
    }

    #[test]
    fn macos_options_validate_before_creating_a_window() {
        assert!(configure_window(WindowBuilder::new(), &json!({"titleBarStyle":"Overlay", "hiddenTitle":true, "trafficLightPosition":{"x":12,"y":16}})).is_ok());
        assert!(configure_window(WindowBuilder::new(), &json!({"titleBarStyle":42})).is_err());
        assert!(
            configure_window(
                WindowBuilder::new(),
                &json!({"trafficLightPosition":{"x":12}})
            )
            .is_err()
        );
        assert!(
            configure_window(
                WindowBuilder::new(),
                &json!({"trafficLightPosition":{"x":"bad","y":12}})
            )
            .is_err()
        );
        assert!(
            configure_window(
                WindowBuilder::new(),
                &json!({"trafficLightPosition":null,"tabbingIdentifier":null,"hiddenTitle":null})
            )
            .is_ok()
        );
        assert!(configure_window(WindowBuilder::new(), &json!({"hiddenTitle":"yes"})).is_err());
        assert!(configure_window(WindowBuilder::new(), &json!({"tabbingIdentifier":42})).is_err());
        assert!(material(WindowEffect::Sidebar).is_some());
        assert!(material(WindowEffect::Mica).is_none());
    }
}
