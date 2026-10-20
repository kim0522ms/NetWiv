# C# menu API

Frontend menu types are exposed through `@netwiv/api/menu`. Their private
implementation uses the upstream menu protocol pinned in
`compatibility/upstream-lock.json`: TAURI® runtime 2.11.5 and `@tauri-apps/api` 2.11.1.
The implementation writes the upstream command names and JSON shapes directly; it does not use
reflection-based serialization.

The upstream contract is an internal comparison reference, not a supported
alternative frontend entry point; see [frontend SDK](frontend-api.md).

## Icon menu items

`IconMenuItem` has the same common operations as `MenuItem` (`TextAsync`, `SetTextAsync`,
`IsEnabledAsync`, `SetEnabledAsync`, and `SetAcceleratorAsync`) and adds `SetIconAsync`.

```csharp
var image = await Image.FromPathAsync(app.Handle, "icons/open.png");
var open = await IconMenuItem.NewAsync(
    app.Handle,
    new IconMenuItemOptions(
        Text: "Open",
        Id: "open",
        Accelerator: "Ctrl+O",
        Icon: image));

await open.SetIconAsync(image);
await open.SetIconAsync(null);
```

`MenuIcon` is the typed union used by icon menu items and submenus. An `Image` or `NativeIcon`
converts to it implicitly, so callers normally do not need to construct it directly:

```csharp
var preferences = await IconMenuItem.NewAsync(
    app.Handle,
    new IconMenuItemOptions("Preferences", Icon: NativeIcon.PreferencesGeneral));

await preferences.SetIconAsync(NativeIcon.Advanced);
```

The `NativeIcon` members match the pinned JavaScript enum exactly. Upstream Tauri documents native
icons as unsupported on Windows and Linux. Use an `Image` resource for portable menu icons. Create
that resource with `Image.FromPathAsync`, `Image.FromBytesAsync`, or `Image.NewAsync` before passing
it to a menu option.

Values returned by `Menu.GetAsync`, `Menu.ItemsAsync`, and `Menu.RemoveAtAsync` with kind `Icon` are
materialized as `IconMenuItem`, so icon-specific operations remain available. A future item kind is
materialized as `MenuItem` while preserving its wire kind instead of being mislabeled as
`MenuItem`.

## About predefined item

Use `PredefinedMenuItemType.About` and place its optional metadata in the `About` property. This
writes the upstream externally-tagged shape `{ "About": metadata }`:

```csharp
var icon = await Image.FromPathAsync(app.Handle, "icons/app.png");
var about = await PredefinedMenuItem.NewAsync(
    app.Handle,
    new PredefinedMenuItemOptions(
        Item: PredefinedMenuItemType.About,
        Text: "About NetWiv",
        About: new AboutMetadata(
            Name: "NetWiv",
            Version: "2.11.5",
            ShortVersion: "2.11",
            Authors: ["NetWiv contributors"],
            License: "Apache-2.0",
            Website: "https://example.com/netwiv",
            WebsiteLabel: "NetWiv project",
            Icon: icon)));
```

Passing no metadata writes `{ "About": null }`, which asks the native implementation to use its
default application metadata:

```csharp
var about = await PredefinedMenuItem.NewAsync(
    app.Handle,
    new PredefinedMenuItemOptions(PredefinedMenuItemType.About));
```

`AboutMetadata` exposes all pinned fields: `Name`, `Version`, `ShortVersion`, `Authors`, `Comments`,
`Copyright`, `License`, `Website`, `WebsiteLabel`, `Credits`, and `Icon`. The icon is a managed
`Image` resource. The native host resolves that resource and passes the metadata to muda. On
Windows, muda displays the name, combined version, authors, license, website, comments, and
copyright; it does not display credits or the icon. On Linux, it also displays the icon and does
not display credits. These exclusions match the pinned upstream behavior.

## macOS application menus

The Wry backend installs a default AppKit application menu. macOS has one
application menu bar: the focused window's menu takes precedence over the
application menu. The backend restores the applicable menu when focus or window
ownership changes. Context menus use the owning native view.

The official guest methods `Submenu.setAsHelpMenuForNSApp` and
`Submenu.setAsWindowsMenuForNSApp` forward to muda's NSApplication roles.
Attach the submenu to the current application menu before assigning its role;
muda resolves the live AppKit submenu after attachment. A role call returning
success for an unattached submenu does not establish that macOS assigned it.
`NativeIcon` values and About metadata, including credits, are forwarded on
macOS. Managed and NativeAOT runs on physical macOS arm64 verified Help Search,
window-menu switching, About name/version/credits, tray template/color/icon
replacement, left/right menu actions and cleanup. Other roles, system icons and
About fields remain unverified. See [API status](api-status.md) and
[macOS limits](macos.md#validation-and-limits).

The maintained [menu/tray smoke](../tests/NetWiv.MenuTray.Smoke/README.md) uses
unchanged pinned guest modules. It requires real menu and tray selections,
then separate manual confirmation of the Help Search field, Window switching,
About credits and template-icon rendering. Its managed build and wire tests
alone do not verify those native presentation and input behaviors.

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.
WRY is a trademark of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.
