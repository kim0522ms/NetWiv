use super::{NativeResources, creation_channel};
use super::menus::about_metadata;
use serde_json::json;
#[cfg(not(target_os = "macos"))]
use serde_json::Value;

#[test]
fn encoded_image_fixtures_preserve_pixels_and_dimensions() {
    for (bytes, expected) in [
        (include_bytes!("../../../../tests/conformance/frontend/images/rgba.png").as_slice(), vec![255,0,0,255,0,255,0,128]),
        (include_bytes!("../../../../tests/conformance/frontend/images/palette.png").as_slice(), vec![255,0,0,255,0,255,0,128]),
        (include_bytes!("../../../../tests/conformance/frontend/images/gray16.png").as_slice(), vec![0,0,0,255,128,128,128,255,255,255,255,255]),
        (include_bytes!("../../../../tests/conformance/frontend/images/multi.ico").as_slice(), vec![255,0,0,255,0,255,0,128]),
    ] {
        let image = super::images::decode_image(bytes).unwrap();
        assert_eq!(image.height, 1);
        assert_eq!(image.width as usize, expected.len() / 4);
        assert_eq!(image.rgba, expected);
    }
}

#[test]
fn image_decode_errors_match_tauri_and_do_not_allocate_resources() {
    let mut resources = NativeResources::new();
    for (bytes, expected) in [
        (vec![], "failed to process image: The image format could not be determined"),
        (vec![1,2,3], "failed to process image: The image format could not be determined"),
        (include_bytes!("../../../../tests/conformance/frontend/images/rgba.png")[..16].to_vec(), "failed to process image: unexpected end of file"),
    ] {
        assert_eq!(resources.invoke("plugin:image|from_bytes", &json!({"bytes":bytes}), "main", 1).unwrap_err(), expected);
        assert!(resources.resources.is_empty());
    }
    let path = std::env::temp_dir().join(format!("netwiv-missing-image-{}", std::process::id())).join("missing.png");
    let expected = std::fs::read(&path).unwrap_err().to_string();
    assert_eq!(resources.invoke("plugin:image|from_path", &json!({"path":path}), "main", 1).unwrap_err(), expected);
    assert!(resources.resources.is_empty());
}

#[test]
fn invalid_resource_errors_match_tauri() {
    let mut resources = NativeResources::new();
    assert_eq!(
        resources.take_channel(41).unwrap_err(),
        "The resource id 41 is invalid."
    );
}

#[test]
fn resource_handles_survive_wrong_kind_access_and_are_not_reused_after_close() {
    let mut resources = NativeResources::new();
    let rid = resources.insert_image(vec![1, 2, 3, 255], 1, 1).unwrap();
    for command in ["plugin:menu|is_checked", "plugin:tray|set_visible"] {
        assert_eq!(
            resources.invoke(command, &json!({"rid":rid,"visible":true}), "main", 1).unwrap_err(),
            format!("The resource id {rid} is invalid.")
        );
    }
    assert_eq!(resources.invoke("plugin:image|rgba", &json!({"rid":rid}), "main", 1).unwrap(), json!([1, 2, 3, 255]));
    resources.invoke("plugin:resources|close", &json!({"rid":rid}), "main", 1).unwrap();
    let next = resources.insert_image(vec![4, 5, 6, 255], 1, 1).unwrap();
    assert_ne!(rid, next);
    assert!(resources.invoke("plugin:image|size", &json!({"rid":rid}), "main", 1).is_err());
    assert_eq!(resources.invoke("plugin:image|rgba", &json!({"rid":next}), "main", 1).unwrap(), json!([4, 5, 6, 255]));
}

#[test]
fn exhausted_resource_ids_never_overwrite_live_resources() {
    let mut resources = NativeResources::new();
    // Reach the native/managed RID boundary without allocating billions of objects.
    resources.next_rid = 0x7fff_ffff;
    let rid = resources.insert_image(vec![1, 2, 3, 255], 1, 1).unwrap();
    assert_eq!(rid, 0x7fff_ffff);
    assert_eq!(resources.insert_image(vec![4, 5, 6, 255], 1, 1).unwrap_err(), "native resource id space exhausted");
    assert_eq!(resources.invoke("plugin:image|rgba", &json!({"rid":rid}), "main", 1).unwrap(), json!([1, 2, 3, 255]));
    resources.invoke("plugin:resources|close", &json!({"rid":rid}), "main", 1).unwrap();
    assert_eq!(resources.insert_image(vec![4, 5, 6, 255], 1, 1).unwrap_err(), "native resource id space exhausted");
}

#[test]
fn image_bytes_round_trip_and_close_invalidates_the_resource() {
    let mut encoded = Vec::new();
    {
        let mut encoder = png::Encoder::new(&mut encoded, 2, 1);
        encoder.set_color(png::ColorType::Rgb);
        encoder.set_depth(png::BitDepth::Eight);
        let mut writer = encoder.write_header().unwrap();
        writer.write_image_data(&[255, 0, 0, 0, 255, 0]).unwrap();
    }

    let mut resources = NativeResources::new();
    let rid = resources
        .invoke(
            "plugin:image|from_bytes",
            &json!({"bytes": encoded}),
            "main",
            1,
        )
        .unwrap()
        .as_u64()
        .unwrap() as u32;
    assert_eq!(
        resources
            .invoke("plugin:image|size", &json!({"rid":rid}), "main", 1)
            .unwrap(),
        json!({"width":2,"height":1})
    );
    assert_eq!(
        resources
            .invoke("plugin:image|rgba", &json!({"rid":rid}), "main", 1)
            .unwrap(),
        json!([255, 0, 0, 255, 0, 255, 0, 255])
    );
    resources
        .invoke("plugin:resources|close", &json!({"rid":rid}), "main", 1)
        .unwrap();
    assert_eq!(
        resources
            .invoke("plugin:image|size", &json!({"rid":rid}), "main", 1)
            .unwrap_err(),
        format!("The resource id {rid} is invalid.")
    );
}

#[test]
fn guest_tray_argument_names_are_enforced_before_resource_lookup() {
    let mut resources = NativeResources::new();
    let invalid = json!({"rid":9,"isTemplate":true});
    assert_eq!(
        resources
            .invoke("plugin:tray|set_icon_as_template", &invalid, "main", 1)
            .unwrap_err(),
        "command argument 'asTemplate' must be a boolean"
    );
    let valid = json!({"rid":9,"asTemplate":true});
    assert_eq!(
        resources
            .invoke("plugin:tray|set_icon_as_template", &valid, "main", 1)
            .unwrap_err(),
        "The resource id 9 is invalid."
    );

    let invalid = json!({"rid":9,"value":false});
    assert_eq!(
        resources
            .invoke(
                "plugin:tray|set_show_menu_on_left_click",
                &invalid,
                "main",
                1,
            )
            .unwrap_err(),
        "command argument 'onLeft' must be a boolean"
    );
}

#[test]
fn menu_and_tray_new_require_the_channels_sent_by_the_guest_api() {
    let mut resources = NativeResources::new();
    assert!(
        creation_channel(&json!({}), "main", 1, true)
            .unwrap()
            .is_none()
    );
    assert_eq!(
        creation_channel(&json!({}), "main", 1, false).unwrap_err(),
        "'handler' must be a Tauri channel"
    );
    assert_eq!(
        resources
            .invoke(
                "plugin:menu|new",
                &json!({"kind":"Menu","options":null}),
                "main",
                1,
            )
            .unwrap_err(),
        "'handler' must be a Tauri channel"
    );
    assert_eq!(
        resources
            .invoke("plugin:tray|new", &json!({"options":{}}), "main", 1)
            .unwrap_err(),
        "'handler' must be a Tauri channel"
    );
}

#[test]
fn malformed_nested_menu_payload_is_rejected_before_native_creation() {
    let mut resources = NativeResources::new();
    assert_eq!(
        resources
            .resolve_item(&json!({"checked":true}), "main", 1)
            .unwrap_err(),
        "data did not match any variant of untagged enum MenuItemPayloadKind"
    );
    assert_eq!(resources.resources.len(), 0);
}

#[test]
fn about_metadata_maps_camel_case_fields_and_resolves_image_resources() {
    let mut encoded = Vec::new();
    {
        let mut encoder = png::Encoder::new(&mut encoded, 1, 1);
        encoder.set_color(png::ColorType::Rgba);
        encoder.set_depth(png::BitDepth::Eight);
        let mut writer = encoder.write_header().unwrap();
        writer.write_image_data(&[1, 2, 3, 255]).unwrap();
    }
    let mut resources = NativeResources::new();
    let icon_rid = resources
        .invoke(
            "plugin:image|from_bytes",
            &json!({"bytes":encoded}),
            "main",
            1,
        )
        .unwrap()
        .as_u64()
        .unwrap();

    let metadata = about_metadata(
        &resources,
        &json!({
            "name":"NetWiv",
            "version":"2.11.5",
            "shortVersion":"2.11",
            "authors":["Alice","Bob"],
            "comments":"Desktop runtime",
            "copyright":"Copyright 2026",
            "license":"MIT OR Apache-2.0",
            "website":"https://example.invalid",
            "websiteLabel":"Project site",
            "credits":"Contributors",
            "icon":icon_rid
        }),
    )
    .unwrap();

    assert_eq!(metadata.name.as_deref(), Some("NetWiv"));
    assert_eq!(metadata.version.as_deref(), Some("2.11.5"));
    assert_eq!(metadata.short_version.as_deref(), Some("2.11"));
    assert_eq!(metadata.authors, Some(vec!["Alice".into(), "Bob".into()]));
    assert_eq!(metadata.comments.as_deref(), Some("Desktop runtime"));
    assert_eq!(metadata.copyright.as_deref(), Some("Copyright 2026"));
    assert_eq!(metadata.license.as_deref(), Some("MIT OR Apache-2.0"));
    assert_eq!(metadata.website.as_deref(), Some("https://example.invalid"));
    assert_eq!(metadata.website_label.as_deref(), Some("Project site"));
    assert_eq!(metadata.credits.as_deref(), Some("Contributors"));
    assert!(metadata.icon.is_some());
}

#[test]
fn about_metadata_matches_serde_absent_null_and_wrong_type_behavior() {
    let resources = NativeResources::new();
    let absent = about_metadata(&resources, &json!({})).unwrap();
    assert!(absent.name.is_none());
    assert!(absent.authors.is_none());
    assert!(absent.icon.is_none());

    let null =
        about_metadata(&resources, &json!({"name":null,"authors":null,"icon":null})).unwrap();
    assert!(null.name.is_none());
    assert!(null.authors.is_none());
    assert!(null.icon.is_none());

    assert_eq!(
        about_metadata(&resources, &json!("not an object")).unwrap_err(),
        "about metadata must be an object"
    );
    assert_eq!(
        about_metadata(&resources, &json!({"name":7})).unwrap_err(),
        "'name' must be a string or null"
    );
    assert_eq!(
        about_metadata(&resources, &json!({"authors":"Alice"})).unwrap_err(),
        "about metadata authors must be an array"
    );
    assert_eq!(
        about_metadata(&resources, &json!({"authors":["Alice",7]})).unwrap_err(),
        "about metadata authors must be strings"
    );
    assert_eq!(
        about_metadata(&resources, &json!({"icon":999})).unwrap_err(),
        "The resource id 999 is invalid."
    );
}

#[cfg(not(target_os = "macos"))]
#[test]
fn nsapp_only_menu_commands_are_noops_on_desktop_targets() {
    let mut resources = NativeResources::new();
    for command in [
        "plugin:menu|set_as_windows_menu_for_nsapp",
        "plugin:menu|set_as_help_menu_for_nsapp",
    ] {
        assert_eq!(
            resources.invoke(command, &json!({"rid":77}), "main", 1),
            Ok(Value::Null)
        );
    }
}

#[cfg(target_os = "windows")]
#[test]
fn menu_enumeration_returns_independent_handles_and_remove_at_is_nullable() {
    let mut resources = NativeResources::new();
    let menu = resources
        .invoke(
            "plugin:menu|new",
            &json!({
                "kind":"Menu",
                "options":{"id":"root"},
                "handler":"__CHANNEL__:1"
            }),
            "main",
            1,
        )
        .unwrap()[0]
        .as_u64()
        .unwrap() as u32;
    let item = resources
        .invoke(
            "plugin:menu|new",
            &json!({
                "kind":"MenuItem",
                "options":{"id":"child","text":"Child"},
                "handler":"__CHANNEL__:2"
            }),
            "main",
            1,
        )
        .unwrap()[0]
        .as_u64()
        .unwrap() as u32;
    resources
        .invoke(
            "plugin:menu|append",
            &json!({"rid":menu,"kind":"Menu","items":[[item,"MenuItem"]]}),
            "main",
            1,
        )
        .unwrap();
    let first = resources
        .invoke(
            "plugin:menu|items",
            &json!({"rid":menu,"kind":"Menu"}),
            "main",
            1,
        )
        .unwrap();
    let second = resources
        .invoke(
            "plugin:menu|items",
            &json!({"rid":menu,"kind":"Menu"}),
            "main",
            1,
        )
        .unwrap();
    assert_eq!(first[0][1], "child");
    assert_eq!(first[0][2], "MenuItem");
    assert_ne!(first[0][0], second[0][0]);

    let alias = first[0][0].as_u64().unwrap() as u32;
    resources.take_channel(alias).unwrap();
    assert_eq!(
        resources
            .invoke(
                "plugin:menu|text",
                &json!({"rid":item,"kind":"MenuItem"}),
                "main",
                1,
            )
            .unwrap(),
        "Child"
    );
    let removed = resources
        .invoke(
            "plugin:menu|remove_at",
            &json!({"rid":menu,"kind":"Menu","position":0}),
            "main",
            1,
        )
        .unwrap();
    assert_eq!(removed[1], "child");
    assert_eq!(removed[2], "MenuItem");
    assert_eq!(
        resources
            .invoke(
                "plugin:menu|remove_at",
                &json!({"rid":menu,"kind":"Menu","position":0}),
                "main",
                1,
            )
            .unwrap(),
        Value::Null
    );
}

#[cfg(target_os = "windows")]
#[test]
fn detached_check_items_preserve_checked_state_independently_of_enabled() {
    let menu = muda::Menu::new();
    let item = muda::CheckMenuItem::new("Checked", true, false, None);
    let native = super::menus::MenuNative::Check(item.clone());
    assert!(!native.checked().unwrap());
    menu.append(&item).unwrap();
    native.set_checked(true).unwrap();
    assert!(native.checked().unwrap());
    menu.remove(&item).unwrap();
    native.set_enabled(false).unwrap();
    assert!(native.checked().unwrap());
    native.set_checked(false).unwrap();
    native.set_enabled(true).unwrap();
    assert!(!native.checked().unwrap());
}

#[cfg(target_os = "windows")]
#[test]
fn unsupported_predefined_items_remain_in_native_menu_as_disabled_entries() {
    use muda::ContextMenu;
    use windows::Win32::UI::WindowsAndMessaging::{GetMenuItemCount, GetMenuState, HMENU, MF_BYPOSITION, MF_GRAYED};
    let menu = muda::Menu::with_items(&[
        &muda::PredefinedMenuItem::fullscreen(None),
        &muda::PredefinedMenuItem::hide_others(None),
        &muda::PredefinedMenuItem::show_all(None),
        &muda::PredefinedMenuItem::services(None),
        &muda::PredefinedMenuItem::bring_all_to_front(None),
    ]).unwrap();
    let handle = HMENU(menu.hpopupmenu() as _);
    // SAFETY: the native handle belongs to the live test-owned menu.
    unsafe {
        assert_eq!(GetMenuItemCount(Some(handle)), 5);
        for position in 0..5 {
            let state = GetMenuState(handle, position, MF_BYPOSITION);
            assert_ne!(state, u32::MAX);
            assert_ne!(state & MF_GRAYED.0, 0);
        }
    }
}
