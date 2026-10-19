# Linux native dialog probe

This standalone functional host exercises real native dialogs and unchanged
`@tauri-apps/api@2.11.1` core guest IPC. Build the native adapter first as described
in [development](../../docs/development.md). No solution registration is needed.

```sh
node tests/conformance/scripts/build-frontend.mjs
dotnet build tests/NetWiv.Dialog.Smoke -c Release -p:PublishAot=false
bash scripts/run-linux-smoke-desktop.sh "$PWD/tests/NetWiv.Dialog.Smoke/bin/Release/net8.0/NetWiv.Dialog.Smoke" wayland --automatic wayland
dotnet publish tests/NetWiv.Dialog.Smoke -c Release -r linux-x64
bash scripts/run-linux-smoke-desktop.sh "$PWD/tests/NetWiv.Dialog.Smoke/bin/Release/net8.0/linux-x64/publish/NetWiv.Dialog.Smoke" wayland --automatic wayland
```

Use `x11` for both backend arguments to run the existing nested X11 desktop.
Backend identity is asserted from the running native runtime. Automatic mode
opens single/multiple file and folder pickers, save and message dialogs. While
each is pending, it checks UI dispatch and a second native IPC query, cancels
through the caller token, and requires a canceled task. A pre-canceled request
must also fail before submission. Subsequent dialogs,
parent/window destruction, source-WebView destruction, document reload and
application shutdown exercise pending-request cleanup. Reload uses an actual
document generation obtained from guest IPC. A watchdog fails blocked runs.
Do not interact with dialogs during automatic mode.

Run the same managed and NativeAOT commands with `--interactive` instead of
`--automatic` to check native acceptance manually. Follow the eight in-window
steps: single/multiple files, recursive folder, multiple folders, save, custom
message button, user cancel and repeated open. Select only the generated
`alpha.txt`, `beta.txt`, `folder-one`, `folder-two` and `saved.txt` entries.
The sample directory appears only in the UI and is the picker default. The
host validates selection cardinality and exact synthetic paths before the
guest reads synthetic content; it checks selected and recursive scope grants.
A separate denied guest checks that deny wins over allow before completion.
Confirm actual native appearance, responsiveness and dialog closure before
Finish. Closing early or an incomplete check fails.

Temporary synthetic samples are removed on normal exit. Forced process
termination can leave its sample directory. No arbitrary selected file is
read; no selected path, native error details, user input, environment inventory
or report is printed, retained or uploaded. Static console assertions are
functional results. A managed/AOT build or an automatic cancellation run does
not establish manual acceptance. Linux-only results do not validate Windows,
macOS, every portal/compositor, every dialog option or full Tauri equivalence.
