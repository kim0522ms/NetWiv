//! Windows accelerator dispatch, matching the pinned Tauri active-menu stash.
use std::{cell::RefCell, collections::HashMap, ffi::c_void, rc::Rc};

use muda::{Menu, MenuId};
use tao::{event_loop::EventLoopBuilder, platform::windows::EventLoopBuilderExtWindows};
use windows::Win32::UI::WindowsAndMessaging::{HACCEL, MSG, TranslateAcceleratorW};

#[derive(Clone, Default)]
pub(crate) struct MenuAccelerators(Rc<RefCell<HashMap<MenuId, Menu>>>);

impl MenuAccelerators {
    pub(crate) fn install<T>(&self, builder: &mut EventLoopBuilder<T>) {
        let menus = self.clone();
        builder.with_msg_hook(move |message| {
            // SAFETY: tao supplies a live MSG for this synchronous hook.
            unsafe { menus.translate(message) }
        });
    }

    pub(crate) fn replace<'a>(&self, menus: impl Iterator<Item = &'a Menu>) {
        *self.0.borrow_mut() = menus.map(|menu| (menu.id().clone(), menu.clone())).collect();
    }

    unsafe fn translate(&self, message: *const c_void) -> bool {
        let message = message.cast::<MSG>();
        // Copy menu references before dispatch, since WM_COMMAND callbacks may be reentrant.
        let menus: Vec<_> = self.0.borrow().values().cloned().collect();
        for menu in menus {
            // Read the current table: setAccelerator can replace it after menu installation.
            // SAFETY: the caller guarantees a live MSG; the retained menu owns this HACCEL.
            if unsafe { TranslateAcceleratorW((*message).hwnd, HACCEL(menu.haccel() as _), message) } == 1 {
                return true;
            }
        }
        false
    }
}
