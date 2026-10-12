use serde_json::{Value, json};
use tao::dpi::{LogicalPosition, LogicalSize, Position};
use tao::event_loop::EventLoopWindowTarget;
use tao::window::{Fullscreen, WindowBuilder, WindowSizeConstraints};
use crate::model::{Submission, WebviewConfig, WindowConfig, bool_arg, f64_arg, required_string};
use super::{LocalRuntime, UserEvent, WindowState, CALLBACK_EVENT};
use super::menus::attach_menu;
use super::options::{
    invalid_argument,
    size_value,
    position_value,
    user_attention,
    optional_size,
    optional_string,
    optional_bool,
    prevent_overflow_margin,
    size_constraints,
    logical_window_constraints,
    parse_theme,
    theme_name,
    cursor_icon,
    resize_direction,
    optional_color_arg,
    progress_state,
};
use super::platform::{
    set_window_enabled,
    center_window,
    monitor_json,
    monitor_value,
    apply_prevent_overflow,
    set_skip_taskbar,
    set_shadow,
    set_window_effects,
    set_overlay_icon,
    set_badge_count,
};

impl LocalRuntime {
    pub(super) fn create_window(
        &mut self,
        target: &EventLoopWindowTarget<UserEvent>,
        config: WindowConfig,
        dynamic: bool,
        create_implicit_webview: bool,
    ) -> Result<(), String> {
        if self.windows.contains_key(&config.label) {
            return Err(format!("window '{}' already exists", config.label));
        }
        let extra = &config.extra_config;
        let prevent_overflow = prevent_overflow_margin(extra.get("preventOverflow"))?;
        let visible = optional_bool(extra, "visible")?.unwrap_or(true);
        let resizable = optional_bool(extra, "resizable")?.unwrap_or(true);
        let maximizable = optional_bool(extra, "maximizable")?.unwrap_or(true);
        let minimizable = optional_bool(extra, "minimizable")?.unwrap_or(true);
        let closable = optional_bool(extra, "closable")?.unwrap_or(true);
        let focus = optional_bool(extra, "focus")?.unwrap_or(true);
        let focusable = optional_bool(extra, "focusable")?.unwrap_or(true);
        let transparent = optional_bool(extra, "transparent")?.unwrap_or(false);
        let decorations = optional_bool(extra, "decorations")?.unwrap_or(true);
        let always_on_top = optional_bool(extra, "alwaysOnTop")?.unwrap_or(false);
        let always_on_bottom = optional_bool(extra, "alwaysOnBottom")?.unwrap_or(false);
        let visible_on_all_workspaces =
            optional_bool(extra, "visibleOnAllWorkspaces")?.unwrap_or(false);
        let content_protected = optional_bool(extra, "contentProtected")?.unwrap_or(false);
        let maximized = optional_bool(extra, "maximized")?.unwrap_or(false);
        let fullscreen = optional_bool(extra, "fullscreen")?.unwrap_or(false);
        let skip_taskbar = optional_bool(extra, "skipTaskbar")?.unwrap_or(false);
        let shadow = optional_bool(extra, "shadow")?.unwrap_or(true);
        let center = optional_bool(extra, "center")?.unwrap_or(false);
        let enabled = optional_bool(extra, "enabled")?.unwrap_or(true);
        let mut builder = WindowBuilder::new()
            .with_title(config.title.clone())
            .with_inner_size(LogicalSize::new(config.width, config.height))
            .with_window_icon(
                self.resources
                    .window_icon(extra.get("icon"))?
                    .or_else(|| self.default_window_icon.clone()),
            )
            .with_theme(parse_theme(extra.get("theme"))?.or(self.app_theme))
            .with_visible(visible)
            .with_resizable(resizable)
            .with_maximizable(maximizable)
            .with_minimizable(minimizable)
            .with_closable(closable)
            .with_focused(focus)
            .with_focusable(focusable)
            .with_transparent(transparent)
            .with_decorations(decorations)
            .with_always_on_top(always_on_top)
            .with_always_on_bottom(always_on_bottom)
            .with_visible_on_all_workspaces(visible_on_all_workspaces)
            .with_content_protection(content_protected)
            .with_maximized(maximized);
        if let Some(parent_label) = optional_string(extra, "parent")? {
            let parent = self
                .windows
                .get(parent_label)
                .ok_or_else(|| format!("no native parent window has label '{parent_label}'"))?;
            #[cfg(target_os = "windows")]
            {
                use tao::platform::windows::{WindowBuilderExtWindows, WindowExtWindows};
                builder = builder.with_owner_window(parent.window.hwnd());
            }
            #[cfg(target_os = "linux")]
            {
                use tao::platform::unix::{WindowBuilderExtUnix, WindowExtUnix};
                builder = builder.with_transient_for(parent.window.gtk_window());
            }
            #[cfg(target_os = "macos")]
            {
                use tao::platform::macos::{WindowBuilderExtMacOS, WindowExtMacOS};
                builder = builder.with_parent_window(parent.window.ns_window());
            }
        }
        #[cfg(target_os = "macos")]
        { builder = crate::macos::configure_window(builder, extra)?; }
        #[cfg(target_os = "windows")]
        if let Some(classname) = optional_string(extra, "windowClassname")? {
            use tao::platform::windows::WindowBuilderExtWindows;
            builder = builder.with_window_classname(classname);
        }
        if let (Some(x), Some(y)) = (
            extra.get("x").and_then(Value::as_f64),
            extra.get("y").and_then(Value::as_f64),
        ) {
            builder = builder.with_position(LogicalPosition::new(x, y));
        }
        let constraints = logical_window_constraints(extra)?;
        if constraints != WindowSizeConstraints::default() {
            builder = builder.with_inner_size_constraints(constraints);
        }
        if let Some(color) = optional_color_arg(extra, "backgroundColor")? {
            builder = builder.with_background_color(color);
        }
        if fullscreen {
            builder = builder.with_fullscreen(Some(Fullscreen::Borderless(None)));
        }
        let window = builder
            .build(target)
            .map_err(|e| format!("failed to create window '{}': {e}", config.label))?;
        if let Some(menu) = &self.app_menu {
            attach_menu(menu, &window)?;
        }
        if let Some(margin) = prevent_overflow {
            apply_prevent_overflow(&window, margin);
        }
        set_skip_taskbar(&window, skip_taskbar)?;
        set_shadow(&window, shadow)?;
        if let Some(effects) = extra.get("windowEffects") {
            set_window_effects(&window, Some(effects))?;
        }
        if center {
            center_window(&window);
        }
        if !enabled {
            set_window_enabled(&window, false);
        }
        let id = window.id();
        let label = config.label.clone();
        let is_webview_window = create_implicit_webview && config.webviews.is_none();
        let mut state = WindowState {
            normal_size: window.inner_size(),
            normal_position: window.outer_position().unwrap_or_default(),
            window,
            label: label.clone(),
            webviews: Vec::new(),
            enabled,
            menu: None,
        };
        let webviews = config.webviews.unwrap_or_else(|| {
            if create_implicit_webview {
                vec![WebviewConfig {
                    label: label.clone(),
                    url: config.url,
                    x: 0.0,
                    y: 0.0,
                    width: config.width,
                    height: config.height,
                    extra_config: config.extra_config,
                }]
            } else {
                Vec::new()
            }
        });
        for webview in webviews {
            self.create_webview_in(&mut state, webview, dynamic, is_webview_window)?;
        }
        self.window_ids.insert(id, label.clone());
        self.windows.insert(label.clone(), state);
        if dynamic {
            self.event(
                "tauri://window-created",
                Some(&label),
                json!({"label":label,"isWebviewWindow":is_webview_window}),
                "Window",
            );
        }
        Ok(())
    }

    pub(super) fn invoke_window(&mut self, command: &str, submission: &Submission) -> Result<Value, String> {
        let label = submission
            .args
            .get("label")
            .and_then(Value::as_str)
            .or(submission.window_label.as_deref())
            .or_else(|| {
                self.webviews
                    .get(&submission.webview_label)
                    .map(|v| v.window_label.as_str())
            })
            .ok_or_else(|| "window command has no target label".to_string())?
            .to_owned();
        if command == "window.close" || command == "plugin:window|close" {
            if !self.windows.contains_key(&label) {
                return Err("window not found".to_string());
            }
            let prevent = self.shared.callback(CALLBACK_EVENT, 0, &json!({"eventName":"tauri://close-requested","label":label,"payload":null,"target":{"kind":"Window","label":label}}), &[]) == 1;
            if !prevent {
                self.remove_window(&label)?;
            }
            return Ok(Value::Null);
        }
        if command == "plugin:window|destroy" {
            self.remove_window(&label)?;
            return Ok(Value::Null);
        }
        if command == "plugin:window|set_icon" {
            let value = submission
                .args
                .get("value")
                .or_else(|| submission.args.get("icon"));
            let icon = self.resources.window_icon(value)?;
            self.windows
                .get(&label)
                .ok_or_else(|| "window not found".to_string())?
                .window
                .set_window_icon(icon);
            return Ok(Value::Null);
        }
        if command == "plugin:window|set_overlay_icon" {
            let icon = self.resources.window_icon(
                submission
                    .args
                    .get("value")
                    .or_else(|| submission.args.get("icon")),
            )?;
            set_overlay_icon(
                &self
                    .windows
                    .get(&label)
                    .ok_or_else(|| "window not found".to_string())?
                    .window,
                icon.as_ref(),
            )?;
            return Ok(Value::Null);
        }
        let state = self
            .windows
            .get_mut(&label)
            .ok_or_else(|| "window not found".to_string())?;
        if matches!(command, "window.maximize" | "plugin:window|maximize" |
            "window.minimize" | "plugin:window|minimize" | "plugin:window|toggle_maximize" |
            "plugin:window|internal_toggle_maximize" | "plugin:window|fullscreen" | "plugin:window|set_fullscreen") {
            state.capture_normal_bounds();
        }
        let window = &state.window;
        match command {
            "window.setTitle" => {
                window.set_title(required_string(&submission.args, "title")?);
                Ok(Value::Null)
            }
            "plugin:window|set_title" => {
                window.set_title(required_string(&submission.args, "value")?);
                Ok(Value::Null)
            }
            "window.show" | "plugin:window|show" => {
                window.set_visible(true);
                Ok(Value::Null)
            }
            "window.hide" | "plugin:window|hide" => {
                window.set_visible(false);
                Ok(Value::Null)
            }
            "window.minimize" | "plugin:window|minimize" => {
                window.set_minimized(true);
                Ok(Value::Null)
            }
            "window.maximize" | "plugin:window|maximize" => {
                window.set_maximized(true);
                Ok(Value::Null)
            }
            "plugin:window|unmaximize" => {
                window.set_maximized(false);
                Ok(Value::Null)
            }
            "plugin:window|unminimize" => {
                window.set_minimized(false);
                Ok(Value::Null)
            }
            "plugin:window|toggle_maximize" => {
                window.set_maximized(!window.is_maximized());
                Ok(Value::Null)
            }
            "plugin:window|internal_toggle_maximize" => {
                if window.is_resizable() {
                    if window.is_maximized() {
                        window.set_maximized(false);
                    } else if window.is_maximizable() {
                        window.set_maximized(true);
                    }
                }
                Ok(Value::Null)
            }
            "plugin:window|is_maximized" => Ok(json!(window.is_maximized())),
            "plugin:window|is_minimized" => Ok(json!(window.is_minimized())),
            "plugin:window|set_fullscreen" => {
                window.set_fullscreen(
                    bool_arg(&submission.args, "value")?.then_some(Fullscreen::Borderless(None)),
                );
                Ok(Value::Null)
            }
            "plugin:window|fullscreen" => {
                window.set_fullscreen(Some(Fullscreen::Borderless(None)));
                Ok(Value::Null)
            }
            "plugin:window|unfullscreen" => {
                window.set_fullscreen(None);
                Ok(Value::Null)
            }
            "plugin:window|is_fullscreen" => Ok(json!(window.fullscreen().is_some())),
            "plugin:window|set_focus" => {
                window.set_focus();
                Ok(Value::Null)
            }
            "plugin:window|is_focused" => Ok(json!(window.is_focused())),
            "plugin:window|is_visible" => Ok(json!(window.is_visible())),
            "plugin:window|title" => Ok(json!(window.title())),
            "plugin:window|scale_factor" => Ok(json!(window.scale_factor())),
            "plugin:window|is_resizable" => Ok(json!(window.is_resizable())),
            "plugin:window|is_maximizable" => Ok(json!(window.is_maximizable())),
            "plugin:window|is_minimizable" => Ok(json!(window.is_minimizable())),
            "plugin:window|is_closable" => Ok(json!(window.is_closable())),
            "plugin:window|is_enabled" => Ok(json!(state.enabled)),
            "plugin:window|is_always_on_top" => Ok(json!(window.is_always_on_top())),
            "plugin:window|set_resizable" => {
                window.set_resizable(bool_arg(&submission.args, "value")?);
                Ok(Value::Null)
            }
            "plugin:window|set_enabled" => {
                let value = bool_arg(&submission.args, "value")?;
                set_window_enabled(window, value);
                state.enabled = value;
                Ok(Value::Null)
            }
            "plugin:window|set_maximizable" => {
                window.set_maximizable(bool_arg(&submission.args, "value")?);
                Ok(Value::Null)
            }
            "plugin:window|set_minimizable" => {
                window.set_minimizable(bool_arg(&submission.args, "value")?);
                Ok(Value::Null)
            }
            "plugin:window|set_closable" => {
                window.set_closable(bool_arg(&submission.args, "value")?);
                Ok(Value::Null)
            }
            "plugin:window|is_decorated" => Ok(json!(window.is_decorated())),
            "plugin:window|set_decorations" => {
                window.set_decorations(bool_arg(&submission.args, "value")?);
                Ok(Value::Null)
            }
            "plugin:window|set_always_on_top" => {
                window.set_always_on_top(bool_arg(&submission.args, "value")?);
                Ok(Value::Null)
            }
            "plugin:window|set_always_on_bottom" => {
                window.set_always_on_bottom(bool_arg(&submission.args, "value")?);
                Ok(Value::Null)
            }
            "plugin:window|set_content_protected" => {
                window.set_content_protection(bool_arg(&submission.args, "value")?);
                Ok(Value::Null)
            }
            "plugin:window|set_visible_on_all_workspaces" => {
                window.set_visible_on_all_workspaces(bool_arg(&submission.args, "value")?);
                Ok(Value::Null)
            }
            "plugin:window|center" => {
                center_window(window);
                Ok(Value::Null)
            }
            "plugin:window|request_user_attention" => {
                window.request_user_attention(user_attention(submission.args.get("value"))?);
                Ok(Value::Null)
            }
            "plugin:window|set_size" => {
                let (w, h, logical) = size_value(
                    submission
                        .args
                        .get("value")
                        .ok_or("set_size requires value")?,
                )
                .map_err(|error| invalid_argument("set_size", "value", error))?;
                if logical {
                    window.set_inner_size(LogicalSize::new(w, h));
                } else {
                    window.set_inner_size(tao::dpi::PhysicalSize::new(w as u32, h as u32));
                }
                Ok(Value::Null)
            }
            "plugin:window|set_min_size" => {
                window.set_min_inner_size(optional_size(submission.args.get("value"))?);
                Ok(Value::Null)
            }
            "plugin:window|set_max_size" => {
                window.set_max_inner_size(optional_size(submission.args.get("value"))?);
                Ok(Value::Null)
            }
            "plugin:window|set_size_constraints" => {
                window.set_inner_size_constraints(size_constraints(
                    submission
                        .args
                        .get("value")
                        .ok_or("set_size_constraints requires value")?,
                )?);
                Ok(Value::Null)
            }
            "plugin:window|set_position" => {
                let (x, y, logical) = position_value(
                    submission
                        .args
                        .get("value")
                        .ok_or("set_position requires value")?,
                )
                .map_err(|error| invalid_argument("set_position", "value", error))?;
                if logical {
                    window.set_outer_position(LogicalPosition::new(x, y));
                } else {
                    window.set_outer_position(tao::dpi::PhysicalPosition::new(x as i32, y as i32));
                }
                Ok(Value::Null)
            }
            "plugin:window|inner_position" => {
                let p = window.inner_position().map_err(|e| e.to_string())?;
                Ok(json!({"x":p.x,"y":p.y}))
            }
            "plugin:window|outer_position" => {
                let p = window.outer_position().map_err(|e| e.to_string())?;
                Ok(json!({"x":p.x,"y":p.y}))
            }
            "plugin:window|inner_size" => {
                let s = window.inner_size();
                Ok(json!({"width":s.width,"height":s.height}))
            }
            "plugin:window|outer_size" => {
                let s = window.outer_size();
                Ok(json!({"width":s.width,"height":s.height}))
            }
            "plugin:window|current_monitor" => Ok(monitor_json(window.current_monitor())),
            "plugin:window|primary_monitor" => Ok(monitor_json(window.primary_monitor())),
            "plugin:window|monitor_from_point" => Ok(monitor_json(window.monitor_from_point(
                f64_arg(&submission.args, "x")?,
                f64_arg(&submission.args, "y")?,
            ))),
            "plugin:window|available_monitors" => Ok(Value::Array(
                window.available_monitors().map(monitor_value).collect(),
            )),
            "plugin:window|cursor_position" => {
                let p = window.cursor_position().map_err(|e| e.to_string())?;
                Ok(json!({"x":p.x,"y":p.y}))
            }
            "plugin:window|theme" => Ok(json!(theme_name(window.theme()))),
            "plugin:window|set_theme" => {
                window.set_theme(parse_theme(submission.args.get("value"))?);
                Ok(Value::Null)
            }
            "plugin:window|set_focusable" => {
                window.set_focusable(bool_arg(&submission.args, "value")?);
                Ok(Value::Null)
            }
            "plugin:window|set_skip_taskbar" => {
                set_skip_taskbar(window, bool_arg(&submission.args, "value")?)?;
                Ok(Value::Null)
            }
            "plugin:window|set_cursor_grab" => {
                window
                    .set_cursor_grab(bool_arg(&submission.args, "value")?)
                    .map_err(|e| e.to_string())?;
                Ok(Value::Null)
            }
            "plugin:window|set_cursor_visible" => {
                window.set_cursor_visible(bool_arg(&submission.args, "value")?);
                Ok(Value::Null)
            }
            "plugin:window|set_cursor_icon" => {
                window.set_cursor_icon(cursor_icon(required_string(&submission.args, "value")?)?);
                Ok(Value::Null)
            }
            "plugin:window|set_cursor_position" => {
                let (x, y, logical) = position_value(
                    submission
                        .args
                        .get("value")
                        .ok_or("set_cursor_position requires value")?,
                )
                .map_err(|error| invalid_argument("set_cursor_position", "value", error))?;
                let position: Position = if logical {
                    LogicalPosition::new(x, y).into()
                } else {
                    tao::dpi::PhysicalPosition::new(x as i32, y as i32).into()
                };
                window
                    .set_cursor_position(position)
                    .map_err(|e| e.to_string())?;
                Ok(Value::Null)
            }
            "plugin:window|set_ignore_cursor_events" => {
                window
                    .set_ignore_cursor_events(bool_arg(&submission.args, "value")?)
                    .map_err(|e| e.to_string())?;
                Ok(Value::Null)
            }
            "plugin:window|start_dragging" => {
                window.drag_window().map_err(|e| e.to_string())?;
                Ok(Value::Null)
            }
            "plugin:window|start_resize_dragging" => {
                window
                    .drag_resize_window(resize_direction(required_string(
                        &submission.args,
                        "value",
                    )?)?)
                    .map_err(|e| e.to_string())?;
                Ok(Value::Null)
            }
            "plugin:window|set_background_color" => {
                window.set_background_color(
                    optional_color_arg(&submission.args, "value").map_err(|error| {
                        invalid_argument("set_background_color", "value", error)
                    })?,
                );
                Ok(Value::Null)
            }
            "plugin:window|set_progress_bar" => {
                window.set_progress_bar(
                    progress_state(
                        submission
                            .args
                            .get("value")
                            .ok_or("set_progress_bar requires value")?,
                    )
                    .map_err(|error| invalid_argument("set_progress_bar", "value", error))?,
                );
                Ok(Value::Null)
            }
            "plugin:window|set_badge_count" => {
                let count = submission
                    .args
                    .get("value")
                    .map(|value| serde_json::from_value::<Option<i64>>(value.clone()))
                    .transpose()
                    .map_err(|error| invalid_argument("set_badge_count", "value", error))?
                    .flatten();
                let desktop_filename = optional_string(&submission.args, "desktopFilename")?;
                set_badge_count(window, count, desktop_filename);
                Ok(Value::Null)
            }
            "plugin:window|set_badge_label" => {
                let value = optional_string(&submission.args, "value")?;
                #[cfg(target_os = "macos")]
                { use tao::platform::macos::WindowExtMacOS; window.set_badge_label(value.map(str::to_owned)); }
                #[cfg(not(target_os = "macos"))]
                let _ = value;
                Ok(Value::Null)
            }
            "plugin:window|set_shadow" => {
                set_shadow(window, bool_arg(&submission.args, "value")?)?;
                Ok(Value::Null)
            }
            "plugin:window|set_effects" => {
                set_window_effects(window, submission.args.get("value"))?;
                Ok(Value::Null)
            }
            "plugin:window|set_simple_fullscreen" => {
                #[cfg(target_os = "macos")]
                { use tao::platform::macos::WindowExtMacOS; window.set_simple_fullscreen(bool_arg(&submission.args, "value")?); }
                #[cfg(not(target_os = "macos"))]
                window.set_fullscreen(
                    bool_arg(&submission.args, "value")?.then_some(Fullscreen::Borderless(None)),
                );
                Ok(Value::Null)
            }
            "plugin:window|set_title_bar_style" => {
                #[cfg(target_os = "macos")]
                crate::macos::set_title_bar_style(window, submission.args.get("value").ok_or("missing title bar style")?)?;
                Ok(Value::Null)
            },
            _ => Err(format!(
                "native window command '{command}' is not implemented"
            )),
        }
    }
}
