// Auto-resize arithmetic follows tauri-runtime-wry at
// 7cd71369c00978a3783b6ae3e9972358abbe4ae6, crates/tauri-runtime-wry/src/lib.rs.
// Copyright 2019-2024 Tauri Programme within The Commons Conservancy
// SPDX-License-Identifier: Apache-2.0 OR MIT
use tao::dpi::{LogicalPosition, LogicalSize, PhysicalSize, Position, Size};
use wry::Rect;

#[derive(Clone, Copy)]
pub(crate) struct RelativeBounds {
    x: f32,
    y: f32,
    width: f32,
    height: f32,
}

impl RelativeBounds {
    pub(crate) const FULL: Self = Self {
        x: 0.0,
        y: 0.0,
        width: 1.0,
        height: 1.0,
    };

    pub(crate) fn new(bounds: Rect, parent: PhysicalSize<u32>, scale: f64) -> Self {
        let mut relative = Self::FULL;
        relative.set_position(bounds.position, parent, scale);
        relative.set_size(bounds.size, parent, scale);
        relative
    }

    pub(crate) fn set_position(
        &mut self,
        position: Position,
        parent: PhysicalSize<u32>,
        scale: f64,
    ) {
        let position = position.to_logical::<f32>(scale);
        let parent = parent.to_logical::<f32>(scale);
        self.x = position.x / parent.width;
        self.y = position.y / parent.height;
    }

    pub(crate) fn set_size(&mut self, size: Size, parent: PhysicalSize<u32>, scale: f64) {
        let size = size.to_logical::<f32>(scale);
        let parent = parent.to_logical::<f32>(scale);
        self.width = size.width / parent.width;
        self.height = size.height / parent.height;
    }

    pub(crate) fn bounds(self, parent: PhysicalSize<u32>, scale: f64) -> Rect {
        let parent = parent.to_logical::<f32>(scale);
        Rect {
            position: LogicalPosition::new(parent.width * self.x, parent.height * self.y).into(),
            size: LogicalSize::new(parent.width * self.width, parent.height * self.height).into(),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use tao::dpi::PhysicalPosition;

    fn assert_bounds(relative: RelativeBounds, parent: (u32, u32), scale: f64, expected: [i32; 4]) {
        let bounds = relative.bounds(PhysicalSize::new(parent.0, parent.1), scale);
        let position = bounds.position.to_physical::<i32>(scale);
        let size = bounds.size.to_physical::<u32>(scale);
        assert_eq!(
            [
                position.x,
                position.y,
                size.width as i32,
                size.height as i32
            ],
            expected
        );
    }

    #[test]
    fn root_fills_parent_across_resize_and_dpi_changes() {
        for scale in [1.0, 1.25, 1.5, 2.0] {
            assert_bounds(RelativeBounds::FULL, (900, 675), scale, [0, 0, 900, 675]);
            assert_bounds(RelativeBounds::FULL, (320, 240), scale, [0, 0, 320, 240]);
        }
    }

    #[test]
    fn child_retains_ratios_when_growing_and_shrinking() {
        let relative = RelativeBounds::new(
            Rect {
                position: LogicalPosition::new(40, 40).into(),
                size: LogicalSize::new(160, 120).into(),
            },
            PhysicalSize::new(600, 450),
            1.5,
        );
        assert_bounds(relative, (900, 675), 1.5, [90, 90, 360, 270]);
        assert_bounds(relative, (300, 225), 1.5, [30, 30, 120, 90]);
        // A changed DPI changes logical coordinates, not the physical proportions.
        assert_bounds(relative, (900, 675), 2.0, [90, 90, 360, 270]);
    }

    #[test]
    fn setters_update_only_their_own_axes() {
        let parent = PhysicalSize::new(600, 450);
        let mut relative = RelativeBounds::new(
            Rect {
                position: PhysicalPosition::new(60, 45).into(),
                size: LogicalSize::new(100, 50).into(),
            },
            parent,
            1.5,
        );
        relative.set_size(LogicalSize::new(120, 90).into(), parent, 1.5);
        assert_bounds(relative, (900, 675), 1.5, [90, 68, 270, 203]);
        relative.set_position(PhysicalPosition::new(90, 45).into(), parent, 1.5);
        assert_bounds(relative, (900, 675), 1.5, [135, 68, 270, 203]);
    }

    #[test]
    fn enabling_again_captures_current_native_bounds() {
        let relative = RelativeBounds::new(
            Rect {
                position: PhysicalPosition::new(120, 90).into(),
                size: PhysicalSize::new(300, 150).into(),
            },
            PhysicalSize::new(1200, 900),
            1.5,
        );
        assert_bounds(relative, (600, 450), 1.5, [60, 45, 150, 75]);
    }
}
