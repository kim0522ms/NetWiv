# Application lifetime and operating-system activation

`DesktopRuntime` exits when the last window is closed or destroyed by default.
An app can opt into a resident process with `Builder.ExitOnLastWindowClose(false)`:

```csharp
using var app = new Builder()
    .UseRuntime(new NetWiv.Platform.Desktop.DesktopRuntime())
    .UseEmbeddedAssets()
    .ExitOnLastWindowClose(false)
    .Window("main", "My app", "index.html")
    .OnReopen(async (handle, _) =>
    {
        if (handle.GetWindow("main") is { } existing)
        {
            await existing.ShowAsync();
            await existing.UnminimizeAsync();
            await existing.SetFocusAsync();
        }
        else
        {
            await new WebviewWindowBuilder(handle, "main").Url("index.html").BuildAsync();
        }
    })
    .Build();
app.Run();
```

Closing the final window then removes it and its WebViews while keeping the
event loop, plugins and application state alive. Applications choose when to
create another window or call `AppHandle.Exit()`. Explicit exit and restart
retain their existing behavior. `OnCloseRequested` can still prevent an
individual window from closing. App disposal remains the caller's responsibility;
use `using` or the hosting integration as before. The legacy managed Windows and
Linux adapters reject this opt-in; use `NetWiv.Platform.Desktop.DesktopRuntime`.
Existing apps need no migration unless they choose resident behavior.

## Cooperative save and quit

Use `App.RequestExitAsync()` or `AppHandle.RequestExitAsync()` for an
application-owned quit action that must save or ask for confirmation:

```csharp
builder.OnExitRequested(async (handle, request) =>
{
    await SaveAsync(request.CancellationToken);
    if (!CanQuit()) request.PreventDefault();
});
```

The matching `App.OnExitRequested` and `AppHandle.OnExitRequested` hooks can
also register handlers. They run sequentially in registration order; a veto
stops later handlers and returns `false`. Approval submits exit with the requested
exit code and returns `true`. Handlers have no UI-thread affinity; use async
window APIs or `Dispatch` for UI work. Concurrent requests fail with
`InvalidOperationException` rather than sharing cancellation ownership.
Each request snapshots its handlers before running them. Registrations added by
a handler apply to the next request; all registrations end on app disposal.

Caller cancellation throws `OperationCanceledException` and prevents a stale
exit if an ignoring handler later resumes. Save failures leave the app running,
are reported through `EventHandlerFailed`, and propagate to the request caller.
Force exit or app disposal invalidates a pending request and prevents later
handlers or a second exit after the pending handler finishes. Already started
handler work remains application-owned.
Cancellation and invalidation apply before approval; they cannot retract an
exit that has already been approved for native submission.

This hook is only for cooperative application requests. `Exit`, `Restart`, host
shutdown, automatic last-window exit, OS session end and force quit retain their
existing behavior and do not await or consult these handlers. A resident app can
call `RequestExitAsync` from its quit command; existing force-exit callers need
to switch explicitly if they want save/veto behavior. Dispose the app after
`Run` returns as before.

## macOS URL and file activation

`Builder.OnOpened`, `App.OnOpened` and `AppHandle.OnOpened` receive
`OpenedEvent.Urls`, a batch of absolute `Uri` values reported by AppKit/Tao.
File requests use `file:` URLs. Startup activation and requests to a running
app use the same notification. Register handlers with the builder or during
setup so that initial requests can be observed. Windows and Linux use launch
arguments and the single-instance plugin for URL activation; they do not raise
this typed native notification.

Handlers start on the native event-loop thread after setup. They must not block
it. `Task`-returning handlers may overlap; errors are observed through
`AppHandle.EventHandlerFailed`. Registrations end on disposal, which prevents
later handlers from starting; already started tasks are not cancelled or awaited.
No window is selected, created or restored automatically. The underlying
`netwiv://opened` notification has an `App` target and `{urls:[string,...]}`
payload. App or guest emissions cannot invoke the typed native handlers.

`DeepLinkPlugin` filters this native URL batch against
`plugins.deep-link.desktop`'s configured schemes, emits the matching batch as
`deep-link://new-url`, and returns it from `getCurrent()`. URLs outside the configured
schemes do not replace the current deep links. Launch arguments and
single-instance forwarding continue to deliver a single matching URL.
Package the macOS URL schemes in the app bundle configuration; runtime
`register`, `unregister` and `is_registered` remain unsupported on macOS.
Dynamic registration on Windows/Linux does not add a scheme to the plugin's
configured acceptance list.

## Validation scope

The Core harness exercises native-only delivery, URL batches, deep-link filtering,
asynchronous failures and disposal through a fake runtime. The desktop smoke's
`--lifetime-smoke default` and `--lifetime-smoke resident` modes exercise native
last-window shutdown and window recreation. On macOS, `--opened-smoke` sends a
URL AppleEvent from a child process while a window exists and after it has been
destroyed, checking typed delivery, the deep-link event and `getCurrent()`.
Run the affected modes in managed and NativeAOT builds on each target OS.
These checks do not establish Finder/Dock interactions, packaged startup URL/file
activation, file association registration or installed-app acceptance.
The `--exit-request-smoke` mode additionally exercises native async operations
during a cooperative request, a veto, and the following approved request.
