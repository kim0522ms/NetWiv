// Wire contract adapted from Tauri 2.11.5 tray/mod.rs.
// Copyright 2019-2024 Tauri Programme within The Commons Conservancy.
// SPDX-License-Identifier: Apache-2.0 OR MIT
use serde_json::{Value, json};
use tray_icon::TrayIconEvent;

pub(crate) fn serialize(event: &TrayIconEvent) -> Result<Value, String> {
    let rect = match event {
        TrayIconEvent::Click { rect, .. }
        | TrayIconEvent::DoubleClick { rect, .. }
        | TrayIconEvent::Enter { rect, .. }
        | TrayIconEvent::Move { rect, .. }
        | TrayIconEvent::Leave { rect, .. } => rect,
        _ => return Err("Unrecognized native tray event".into()),
    };
    let mut value = serde_json::to_value(event).map_err(|error| error.to_string())?;
    // Tauri's Rect uses tagged dpi::Position/Size. Position converts f64 to i32
    // using dpi's rounding, while the pointer position retains its fraction.
    value["rect"] = json!({
        "position": {"Physical": rect.position.cast::<i32>()},
        "size": {"Physical": rect.size}
    });
    Ok(value)
}

#[cfg(test)]
mod tests {
    use super::*;
    use tray_icon::{MouseButton, MouseButtonState, Rect, TrayIconId};

    #[test]
    fn tray_event_json_serialization() {
        let event = TrayIconEvent::Click {
            button: MouseButton::Left,
            button_state: MouseButtonState::Down,
            id: TrayIconId::new("id"),
            position: Default::default(),
            rect: Rect::default(),
        };
        assert_eq!(
            serialize(&event).unwrap(),
            json!({
                "type":"Click", "button":"Left", "buttonState":"Down", "id":"id",
                "position":{"x":0.0,"y":0.0},
                "rect":{"size":{"Physical":{"width":0,"height":0}},
                    "position":{"Physical":{"x":0,"y":0}}}
            })
        );
    }

    #[test]
    fn all_events_preserve_pointer_fractions_and_round_rect_positions() {
        let id = TrayIconId::new("fractional");
        let position = tao::dpi::PhysicalPosition::new(12.5, -34.25);
        let rect = Rect {
            position: tao::dpi::PhysicalPosition::new(10.5, -30.5),
            size: tao::dpi::PhysicalSize::new(16, 24),
        };
        let events = [
            TrayIconEvent::Click {
                id: id.clone(),
                position,
                rect,
                button: MouseButton::Right,
                button_state: MouseButtonState::Up,
            },
            TrayIconEvent::DoubleClick {
                id: id.clone(),
                position,
                rect,
                button: MouseButton::Middle,
            },
            TrayIconEvent::Enter {
                id: id.clone(),
                position,
                rect,
            },
            TrayIconEvent::Move {
                id: id.clone(),
                position,
                rect,
            },
            TrayIconEvent::Leave { id, position, rect },
        ];
        let mut serialized = Vec::new();
        for (index, event) in events.iter().enumerate() {
            let value = serialize(event).unwrap();
            assert_eq!(
                value["type"],
                ["Click", "DoubleClick", "Enter", "Move", "Leave"][index]
            );
            assert_eq!(value["position"], json!({"x":12.5,"y":-34.25}));
            assert_eq!(
                value["rect"],
                json!({"position":{"Physical":{"x":11,"y":-31}},
                "size":{"Physical":{"width":16,"height":24}}})
            );
            assert_eq!(value.get("button").is_some(), index < 2);
            assert_eq!(value.get("buttonState").is_some(), index == 0);
            serialized.push(value);
        }
        if let Some(path) = std::env::var_os("NETWIV_TRAY_WIRE_OUTPUT") {
            std::fs::write(path, serde_json::to_vec(&serialized).unwrap()).unwrap();
        }
    }
}
