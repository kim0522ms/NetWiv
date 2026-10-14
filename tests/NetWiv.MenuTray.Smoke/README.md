# macOS menu and tray interaction check

This is a functional desktop test, with no saved reports or screenshots. It uses
unchanged `@tauri-apps/api@2.11.1` from the integrity-checked conformance build.
Run in an awake, logged-in macOS desktop after building the native adapter.

```sh
node tests/conformance/scripts/build-frontend.mjs
dotnet run --project tests/NetWiv.MenuTray.Smoke -c Release -p:PublishAot=false
dotnet publish tests/NetWiv.MenuTray.Smoke -c Release -r osx-arm64
caffeinate -d -u -t 600 tests/NetWiv.MenuTray.Smoke/bin/Release/net8.0/osx-arm64/publish/NetWiv.MenuTray.Smoke
```

Use `osx-x64` for Intel. Run both managed and published NativeAOT executables;
publishing alone is not an execution check. No solution registration is required.

The probe's checkbox confirmations are manual assertions. A successful role
setter, image mutation or build does not supply them. Check the actual native UI:

1. Open **Assistance** and search for **Probe action** in **Application**.
   Choose the search result, then select the highlighted **Application → Probe action**
   command. Confirm that the **Help action** counter increases; the search result
   can reveal the command with an arrow without activating it.
   The target command belongs to another menu; this check does not depend on
   AppKit indexing the Help-role menu's own items.
   Confirm that the Search field works. This submenu deliberately has a name
   other than the localized word Help, so AppKit's name heuristic cannot stand
   in for `setAsHelpMenuForNSApp`.
2. Open **Scenes**. Confirm that both probe windows appear as system-added
   entries. Select the secondary window, then switch back using Scenes.
   After `setAsWindowsMenuForNSApp`, setup updates and restores both test-owned
   titles using the public guest `Window.setTitle` API. AppKit registers windows
   on title changes; role assignment alone is not a check that pre-existing
   windows were registered. See [Apple's window-menu registration](https://developer.apple.com/documentation/appkit/nsapplication/addwindowsitem(_:title:filename:)).
3. Open **Application → About menu probe**. Confirm the name, the two supplied
   version values and **NETWIV CREDITS CHECK**, then close the About panel.
4. Inspect the **NW** status item. Its initial red ring must render as a
   monochrome template with transparent center/background. Confirm this, replace
   it with a color icon and inspect the red ring. Mark it as a template and
   inspect the monochrome ring. Replace it with a template square and inspect
   the new monochrome shape. Check each confirmation only after observation.
5. Left-click NW while left-click menus are disabled. Confirm that no menu
   opens and the guest's **Left Down/Up** counters increase. Right-click NW and
   select **Tray action**. The guest must receive the actual native click and
   menu action. Enable left-click menus, left-click NW and select Tray action.
6. Replace the tray menu. Right-click NW, confirm the old action is absent and
   select **Replacement action**. Finish the probe. It restores the previous
   app menu, closes its owned resources and asserts that the tray ID is gone.
   Confirm that NW disappears from the status bar after exit.

`READY` confirms guest setup and successful native calls, not observed menu
roles, rendering or input. Failures exit nonzero; a ten-minute watchdog exits 124. The probe does not
automatically verify AppKit Search, system window entries, About rendering or
template appearance: these need honest manual confirmation. It also does not
establish every predefined menu item, system icon, double/middle click, theme
change, per-window menu precedence, Intel or minimum-OS acceptance. Describe
only the interactions actually completed in the PR; do not retain per-run
reports or environment inventories in the repository.
