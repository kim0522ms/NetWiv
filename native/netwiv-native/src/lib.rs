//! Native desktop adapter for NetWiv.
#![deny(unsafe_op_in_unsafe_fn)]

mod abi;
mod clipboard;
mod dialog;
mod global_shortcut;
mod notification;
#[cfg(target_os = "windows")]
mod menu_accelerators;
mod menu_payload;
#[cfg(target_os = "macos")]
mod macos;
mod model;
mod paths;
mod relative_bounds;
mod resources;
mod stronghold;
mod runtime;
mod tray_payload;

pub use abi::*;

pub const ABI_VERSION: u32 = 3;
