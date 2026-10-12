use serde_json::Value;
use tao::window::Window;
use crate::model::Submission;
use super::LocalRuntime;
use super::options::position_value;

impl LocalRuntime {
    pub(super) fn set_window_menu(&mut self, args: &Value, submission: &Submission) -> Result<Value, String> {
        let label = args
            .get("window")
            .and_then(Value::as_str)
            .or(submission.window_label.as_deref())
            .or_else(|| {
                self.webviews
                    .get(&submission.webview_label)
                    .map(|v| v.window_label.as_str())
            })
            .ok_or("set_as_window_menu has no window")?;
        let Some(state) = self.windows.get_mut(label) else {
            return Ok(Value::Null);
        };
        let menu = self
            .resources
            .root_menu_clone(crate::model::rid_arg(args)?)?;
        let previous = state.menu.clone().or_else(|| self.app_menu.clone());
        replace_attached_menu(previous.as_ref(), &menu, &state.window)?;
        state.menu = Some(menu);
        self.sync_menu_accelerators();
        previous
            .map(|menu| self.resources.add_root_menu_alias(menu))
            .transpose()
            .map(Option::unwrap_or_default)
    }
    pub(super) fn set_app_menu(&mut self, args: &Value) -> Result<Value, String> {
        let menu = self
            .resources
            .root_menu_clone(crate::model::rid_arg(args)?)?;
        for window in self.windows.values() {
            if window.menu.is_none() {
                replace_attached_menu(self.app_menu.as_ref(), &menu, &window.window)?;
            }
        }
        let previous = self.app_menu.replace(menu);
        #[cfg(target_os = "macos")]
        self.sync_macos_menu();
        self.sync_menu_accelerators();
        previous
            .map(|menu| self.resources.add_root_menu_alias(menu))
            .transpose()
            .map(Option::unwrap_or_default)
    }
    #[cfg(target_os = "macos")]
    pub(super) fn sync_macos_menu(&self) {
        if let Some(menu) = self.windows.values().find(|state| state.window.is_focused())
            .and_then(|state| state.menu.as_ref()).or(self.app_menu.as_ref()) {
            menu.init_for_nsapp();
        }
    }

    pub(super) fn sync_menu_accelerators(&self) {
        #[cfg(target_os = "windows")]
        self.menu_accelerators.replace(self.app_menu.iter().chain(self.windows.values().filter_map(|window| window.menu.as_ref())));
    }
    pub(super) fn popup_menu(&self, args: &Value, submission: &Submission) -> Result<Value, String> {
        let label = args
            .get("window")
            .and_then(Value::as_str)
            .or(submission.window_label.as_deref())
            .or_else(|| {
                self.webviews
                    .get(&submission.webview_label)
                    .map(|v| v.window_label.as_str())
            })
            .ok_or("menu popup has no window")?;
        let Some(window) = self.windows.get(label).map(|state| &state.window) else {
            return Ok(Value::Null);
        };
        let menu = self.resources.popup_menu_clone(args)?;
        let at: Option<muda::dpi::Position> = args
            .get("at")
            .map(|v| {
                position_value(v).map(|(x, y, logical)| {
                    if logical {
                        muda::dpi::LogicalPosition::new(x, y).into()
                    } else {
                        muda::dpi::PhysicalPosition::new(x as i32, y as i32).into()
                    }
                })
            })
            .transpose()?;
        #[cfg(target_os = "windows")]
        {
            use tao::platform::windows::WindowExtWindows;
            unsafe { menu.show_context_menu_for_hwnd(window.hwnd(), at) };
        }
        #[cfg(target_os = "linux")]
        {
            use gtk::prelude::Cast;
            use tao::platform::unix::WindowExtUnix;
            menu.show_context_menu_for_gtk_window(
                window.gtk_window().upcast_ref::<gtk::Window>(),
                at,
            );
        }
        #[cfg(target_os = "macos")]
        {
            use tao::platform::macos::WindowExtMacOS;
            // SAFETY: this view belongs to the live UI-thread window.
            unsafe { menu.show_context_menu_for_nsview(window.ns_view(), at); }
        }
        Ok(Value::Null)
    }
}

pub(super) fn replace_attached_menu(previous: Option<&muda::Menu>, next: &muda::Menu, window: &Window) -> Result<(), String> {
    #[cfg(target_os = "macos")]
    if !window.is_focused() { return Ok(()); }
    if let Some(previous) = previous {
        detach_menu(previous, window)?;
    }
    attach_menu(next, window)
}

pub(super) fn detach_menu(menu: &muda::Menu, window: &Window) -> Result<(), String> {
    #[cfg(target_os = "macos")]
    { let _ = window; menu.remove_for_nsapp(); Ok(()) }
    #[cfg(target_os = "windows")]
    {
        use tao::platform::windows::WindowExtWindows;
        // SAFETY: the runtime owns this live window and attached menu on its UI thread.
        unsafe { menu.remove_for_hwnd(window.hwnd()) }.map_err(|error| error.to_string())
    }
    #[cfg(target_os = "linux")]
    {
        use tao::platform::unix::WindowExtUnix;
        menu.remove_for_gtk_window(window.gtk_window()).map_err(|error| error.to_string())
    }
}

pub(super) fn attach_menu(menu: &muda::Menu, window: &Window) -> Result<(), String> {
    #[cfg(target_os = "macos")]
    { let _ = window; menu.init_for_nsapp(); Ok(()) }
    #[cfg(target_os = "windows")]
    {
        use tao::platform::windows::WindowExtWindows;
        unsafe { menu.init_for_hwnd(window.hwnd()) }.map_err(|e| e.to_string())
    }
    #[cfg(target_os = "linux")]
    {
        use tao::platform::unix::WindowExtUnix;
        menu.init_for_gtk_window(window.gtk_window(), window.default_vbox())
            .map_err(|e| e.to_string())
    }
}
