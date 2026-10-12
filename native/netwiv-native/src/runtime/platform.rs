use serde_json::{Value, json};
use tao::event_loop::EventLoopWindowTarget;
use tao::monitor::MonitorHandle;
use tao::window::Window;
use super::UserEvent;
use super::options::{invalid_argument, constrained_inner_size};


#[cfg(target_os = "linux")]
pub(super) fn platform_backend(target: &EventLoopWindowTarget<UserEvent>) -> &'static str {
    use tao::platform::unix::EventLoopWindowTargetExtUnix;
    if target.is_wayland() {
        "wayland"
    } else {
        "x11"
    }
}

#[cfg(target_os = "windows")]
pub(super) fn platform_backend(_: &EventLoopWindowTarget<UserEvent>) -> &'static str {
    "windows"
}

#[cfg(target_os = "macos")]
pub(super) fn platform_backend(_: &EventLoopWindowTarget<UserEvent>) -> &'static str { "macos" }

#[cfg(target_os = "macos")]
fn monitor_work_area(monitor: &MonitorHandle) -> (tao::dpi::PhysicalPosition<i32>, tao::dpi::PhysicalSize<u32>) {
    crate::macos::monitor_work_area(monitor)
}

pub(super) fn set_window_enabled(window: &Window, enabled: bool) {
    #[cfg(target_os = "macos")]
    { window.set_ignore_cursor_events(!enabled).ok(); window.set_focusable(enabled); }
    #[cfg(target_os = "windows")]
    {
        use tao::platform::windows::WindowExtWindows;
        window.set_enable(enabled);
    }
    #[cfg(target_os = "linux")]
    {
        use gtk::prelude::*;
        use tao::platform::unix::WindowExtUnix;
        window.gtk_window().set_sensitive(enabled);
    }
}

pub(super) fn center_window(window: &Window) {
    let Some(monitor) = window.current_monitor() else {
        return;
    };
    let (work_position, work_size) = monitor_work_area(&monitor);
    let size = window.outer_size();
    let x = (i64::from(work_size.width) - i64::from(size.width)) / 2 + i64::from(work_position.x);
    let y = (i64::from(work_size.height) - i64::from(size.height)) / 2 + i64::from(work_position.y);
    window.set_outer_position(tao::dpi::PhysicalPosition::new(
        x.clamp(i64::from(i32::MIN), i64::from(i32::MAX)) as i32,
        y.clamp(i64::from(i32::MIN), i64::from(i32::MAX)) as i32,
    ));
}

pub(super) fn monitor_json(monitor: Option<MonitorHandle>) -> Value {
    monitor.map(monitor_value).unwrap_or(Value::Null)
}

pub(super) fn monitor_value(monitor: MonitorHandle) -> Value {
    let position = monitor.position();
    let size = monitor.size();
    let (work_position, work_size) = monitor_work_area(&monitor);
    json!({
        "name":monitor.name(),
        "position":{"x":position.x,"y":position.y},
        "size":{"width":size.width,"height":size.height},
        "workArea":{"position":{"x":work_position.x,"y":work_position.y},"size":{"width":work_size.width,"height":work_size.height}},
        "scaleFactor":monitor.scale_factor()
    })
}

#[cfg(target_os = "windows")]
fn monitor_work_area(
    monitor: &MonitorHandle,
) -> (tao::dpi::PhysicalPosition<i32>, tao::dpi::PhysicalSize<u32>) {
    use tao::platform::windows::MonitorHandleExtWindows;
    use windows::Win32::Graphics::Gdi::{GetMonitorInfoW, HMONITOR, MONITORINFO};
    let mut info = MONITORINFO {
        cbSize: std::mem::size_of::<MONITORINFO>() as u32,
        ..Default::default()
    };
    // SAFETY: hmonitor is owned by tao and info has the size expected by GetMonitorInfoW.
    if unsafe { GetMonitorInfoW(HMONITOR(monitor.hmonitor() as _), &mut info) }.as_bool() {
        (
            tao::dpi::PhysicalPosition::new(info.rcWork.left, info.rcWork.top),
            tao::dpi::PhysicalSize::new(
                (info.rcWork.right - info.rcWork.left) as u32,
                (info.rcWork.bottom - info.rcWork.top) as u32,
            ),
        )
    } else {
        (monitor.position(), monitor.size())
    }
}

#[cfg(target_os = "linux")]
fn monitor_work_area(
    monitor: &MonitorHandle,
) -> (tao::dpi::PhysicalPosition<i32>, tao::dpi::PhysicalSize<u32>) {
    use gtk::prelude::MonitorExt;
    use tao::platform::unix::MonitorHandleExtUnix;
    let rect = monitor.gdk_monitor().workarea();
    let scale = monitor.scale_factor();
    (
        LogicalPosition::new(rect.x(), rect.y()).to_physical(scale),
        LogicalSize::new(rect.width() as u32, rect.height() as u32).to_physical(scale),
    )
}

pub(super) fn apply_prevent_overflow(window: &Window, margin: tao::dpi::PhysicalSize<u32>) {
    let Some(monitor) = window.current_monitor() else {
        return;
    };
    let (_, work_area) = monitor_work_area(&monitor);
    let inner = window.inner_size();
    let constrained = constrained_inner_size(inner, window.outer_size(), work_area, margin);
    if constrained != inner {
        window.set_inner_size(constrained);
    }
}

#[cfg(target_os = "macos")]
pub(super) fn set_skip_taskbar(_: &Window, skip: bool) -> Result<(), String> {
    if skip { Err("Per-window skipTaskbar is unsupported on macOS; the Dock represents the application.".into()) } else { Ok(()) }
}

#[cfg(not(target_os = "macos"))]
pub(super) fn set_skip_taskbar(window: &Window, skip: bool) -> Result<(), String> {
    #[cfg(target_os = "linux")]
    use tao::platform::unix::WindowExtUnix;
    #[cfg(target_os = "windows")]
    use tao::platform::windows::WindowExtWindows;
    window
        .set_skip_taskbar(skip)
        .map_err(|error| error.to_string())
}

pub(super) fn set_shadow(window: &Window, enabled: bool) -> Result<(), String> {
    #[cfg(target_os = "macos")]
    { use tao::platform::macos::WindowExtMacOS; window.set_has_shadow(enabled); Ok(()) }
    #[cfg(target_os = "windows")]
    {
        use tao::platform::windows::WindowExtWindows;
        window.set_undecorated_shadow(enabled);
        Ok(())
    }
    #[cfg(target_os = "linux")]
    {
        let _ = (window, enabled);
        Ok(())
    }
}

pub(super) fn decode_window_effects(value: Option<&Value>) -> Result<Option<tauri_utils::config::WindowEffectsConfig>, String> {
    // Decode before selecting the platform path: even Linux's no-op validates
    // the complete public argument, including Apple-only fields and unknown keys.
    serde_json::from_value(value.cloned().unwrap_or(Value::Null))
        .map_err(|error| invalid_argument("set_effects", "value", error))
}

pub(super) fn set_window_effects(window: &Window, value: Option<&Value>) -> Result<(), String> {
    let config = decode_window_effects(value)?;
    #[cfg(target_os = "macos")]
    crate::macos::set_effects(window, config)?;
    #[cfg(target_os = "windows")]
    {
        use tauri_utils::WindowEffect;
        let Some(config) = config else {
            let _ = window_vibrancy::clear_blur(window);
            let _ = window_vibrancy::clear_acrylic(window);
            let _ = window_vibrancy::clear_mica(window);
            return Ok(());
        };
        let color = config.color.map(Into::into);
        let effect = config.effects.into_iter().find(|effect| matches!(effect,
            WindowEffect::Blur | WindowEffect::Acrylic | WindowEffect::Mica |
            WindowEffect::MicaDark | WindowEffect::MicaLight | WindowEffect::Tabbed |
            WindowEffect::TabbedDark | WindowEffect::TabbedLight));
        // Pinned Tauri deliberately ignores OS effect-application failures (for
        // example an unavailable Windows effect); retain that observable contract.
        match effect {
            Some(WindowEffect::Blur) => { let _ = window_vibrancy::apply_blur(window, color); }
            Some(WindowEffect::Acrylic) => { let _ = window_vibrancy::apply_acrylic(window, color); }
            Some(WindowEffect::Mica) => { let _ = window_vibrancy::apply_mica(window, None); }
            Some(WindowEffect::MicaDark) => { let _ = window_vibrancy::apply_mica(window, Some(true)); }
            Some(WindowEffect::MicaLight) => { let _ = window_vibrancy::apply_mica(window, Some(false)); }
            Some(WindowEffect::Tabbed) => { let _ = window_vibrancy::apply_tabbed(window, None); }
            Some(WindowEffect::TabbedDark) => { let _ = window_vibrancy::apply_tabbed(window, Some(true)); }
            Some(WindowEffect::TabbedLight) => { let _ = window_vibrancy::apply_tabbed(window, Some(false)); }
            _ => {}
        }
    }
    #[cfg(target_os = "linux")]
    let _ = (window, config);
    Ok(())
}

pub(super) fn set_overlay_icon(window: &Window, icon: Option<&tao::window::Icon>) -> Result<(), String> {
    #[cfg(target_os = "windows")]
    {
        use tao::platform::windows::WindowExtWindows;
        window.set_overlay_icon(icon);
        Ok(())
    }
    #[cfg(any(target_os = "linux", target_os = "macos"))]
    {
        let _ = (window, icon);
        Err("window overlay icons are only supported on Windows".into())
    }
}

pub(super) fn set_badge_count(window: &Window, count: Option<i64>, desktop_filename: Option<&str>) {
    #[cfg(target_os = "macos")]
    {
        use tao::platform::macos::WindowExtMacOS;
        let _ = desktop_filename;
        window.set_badge_label(count.map(|value| value.to_string()));
    }
    #[cfg(target_os = "linux")]
    {
        use tao::platform::unix::WindowExtUnix;
        window.set_badge_count(count, desktop_filename.map(str::to_owned));
    }
    #[cfg(target_os = "windows")]
    let _ = (window, count, desktop_filename);
}
