// Adapt the coordinate values emitted by pinned Wry 0.55.1 to NetWiv's
// WebView-local, top-left, physical-pixel contract. Native weak references avoid
// retaining a WebView through its own drag handler and track live DPI/layout.
#[derive(Default)]
pub(super) struct DragPosition {
    #[cfg(target_os = "linux")]
    view: gtk::glib::WeakRef<webkit2gtk::WebView>,
    #[cfg(target_os = "macos")]
    view: objc2::rc::Weak<objc2_app_kit::NSView>,
}

impl DragPosition {
    pub(super) fn attach(&mut self, native: &wry::WebView) {
        #[cfg(target_os = "linux")]
        {
            use gtk::prelude::*;
            use wry::WebViewExtUnix;
            self.view = native.webview().downgrade();
        }
        #[cfg(target_os = "macos")]
        {
            use wry::WebViewExtMacOS;
            let native_view = native.webview();
            let view: &objc2_app_kit::NSView = &native_view;
            self.view = objc2::rc::Weak::new(view);
        }
        #[cfg(target_os = "windows")]
        let _ = native;
    }

    pub(super) fn physical(&self, position: (i32, i32)) -> Option<(i32, i32)> {
        #[cfg(target_os = "windows")]
        {
            Some(position)
        } // Wry already uses ScreenToClient on the WebView child.
        #[cfg(target_os = "linux")]
        {
            use gtk::prelude::*;
            let view = self.view.upgrade()?;
            Some(scaled(position, f64::from(view.scale_factor())))
        }
        #[cfg(target_os = "macos")]
        {
            use objc2_foundation::NSPoint;
            let view = self.view.load()?;
            // Wry emits (windowX, frameHeight - windowY), before conversion
            // into the receiving NSView. Undo that inversion first.
            let point = window_point(position, view.frame().size.height);
            let local = view.convertPoint_fromView(NSPoint::new(point.0, point.1), None);
            let bounds = view.bounds();
            let flipped = view.isFlipped();
            let top_left = NSPoint::new(
                bounds.origin.x,
                if flipped {
                    bounds.origin.y
                } else {
                    bounds.origin.y + bounds.size.height
                },
            );
            let local = view.convertPointToBacking(local);
            let origin = view.convertPointToBacking(top_left);
            Some(from_backing((local.x, local.y), (origin.x, origin.y)))
        }
    }
}

#[cfg(any(target_os = "linux", test))]
fn scaled(position: (i32, i32), scale: f64) -> (i32, i32) {
    (
        (f64::from(position.0) * scale).round() as i32,
        (f64::from(position.1) * scale).round() as i32,
    )
}

#[cfg(any(target_os = "macos", test))]
fn window_point(position: (i32, i32), frame_height: f64) -> (f64, f64) {
    (f64::from(position.0), frame_height - f64::from(position.1))
}

#[cfg(any(target_os = "macos", test))]
fn from_backing(point: (f64, f64), top_left: (f64, f64)) -> (i32, i32) {
    // AppKit backing coordinates increase upwards even for a flipped NSView;
    // convertPointToBacking has already applied the view's orientation.
    (
        (point.0 - top_left.0).round() as i32,
        (top_left.1 - point.1).round() as i32,
    )
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn gtk_widget_positions_scale_without_adding_window_or_child_offsets() {
        for scale in [1.0, 1.25, 1.5, 2.0] {
            assert_eq!(
                scaled((40, 24), scale),
                ((40.0 * scale) as i32, (24.0 * scale) as i32)
            );
            assert_eq!(
                scaled((-8, -4), scale),
                ((-8.0 * scale) as i32, (-4.0 * scale) as i32)
            );
        }
        assert_eq!(scaled((1, -1), 1.5), (2, -2));
    }

    #[cfg(not(target_os = "windows"))]
    #[test]
    fn construction_without_a_live_native_view_has_no_coordinate_fallback() {
        assert_eq!(DragPosition::default().physical((80, 60)), None);
    }

    #[test]
    fn mac_window_point_undoes_wry_inversion_before_child_conversion() {
        // A view occupying window base (100, 80)..(400, 280), with the
        // cursor at window (140, 250), receives raw Wry (140, -50).
        assert_eq!(window_point((140, -50), 200.0), (140.0, 250.0));
        // AppKit converts into unflipped local (40,170), then backing x2.
        assert_eq!(from_backing((80.0, 340.0), (0.0, 400.0)), (80, 60));
    }

    #[test]
    fn mac_backing_conversion_handles_flipped_views_nonzero_bounds_and_negative_points() {
        // Flipped NSView local y increases down; AppKit backing y decreases.
        assert_eq!(from_backing((100.0, -80.0), (20.0, -20.0)), (80, 60));
        assert_eq!(from_backing((100.0, 360.0), (20.0, 420.0)), (80, 60));
        assert_eq!(from_backing((18.0, 22.0), (20.0, 20.0)), (-2, -2));
        assert_eq!(from_backing((80.25, -60.75), (0.0, 0.0)), (80, 61));
    }

    #[cfg(target_os = "windows")]
    #[test]
    fn windows_preserves_native_physical_client_coordinates() {
        let position = DragPosition::default();
        assert_eq!(position.physical((80, 60)), Some((80, 60)));
        assert_eq!(position.physical((-1, -2)), Some((-1, -2)));
    }
}
