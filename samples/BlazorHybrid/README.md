# Shared Blazor UI: Hybrid development and WebAssembly release

This source sample compiles the same `Shared/App.razor`, its codebehind and
generated typed command client into two hosts. Both call `greet` through
`wwwroot/netwiv.js` → `@netwiv/api/core.invokeJson` → NetWiv IPC. The UI never
receives a desktop backend service directly.

`Debug` defaults to `Hybrid`; other configurations default to `WebAssembly`.
Override either choice with `-p:NetWivBlazorMode=Hybrid` or
`-p:NetWivBlazorMode=WebAssembly`.

Run these commands from the repository root, with the repository's native
Windows artifacts already built as described in `docs/development.md`:

```powershell
dotnet run --project samples/BlazorHybrid/Desktop -c Debug
dotnet run --project samples/BlazorHybrid/Desktop -c Debug -p:NetWivBlazorMode=WebAssembly
dotnet publish samples/BlazorHybrid/Desktop -c Release -r win-x64 -p:PublishAot=true
```

The WebAssembly build publishes the browser frontend before copying its
`wwwroot` into the desktop output. NativeAOT applies to the desktop backend,
not to the browser UI. Its project graph excludes `NetWiv.Blazor` and
`Microsoft.AspNetCore.Components.WebView`. Hybrid requires managed .NET;
publishing Hybrid with `PublishAot=true` fails with an explicit error.

For an explicit managed Hybrid release build:

```powershell
dotnet run --project samples/BlazorHybrid/Desktop -c Release -p:NetWivBlazorMode=Hybrid
```

Hybrid currently targets Windows x64/WebView2. Other desktop systems are not
claimed for this integration. Building or publishing alone does not establish
interactive or debugger acceptance.

Attach a managed .NET debugger to the Hybrid desktop process. Useful breakpoint
locations are `Shared/App.razor` (`Increment`), `Shared/App.razor.cs`
(`GreetAsync`), `Desktop/GreetingCommands.cs` (`GreetingCommands.Greet`,
`GreetingService.CreateGreeting` and `GreetingService.Dispose`) and
`Shared/UiSession.cs` (`Dispose`). Hybrid runs Razor handlers and the backend
in one managed process; WebAssembly executes the Razor handlers in the WebView.

Try Increment, edit the name and Greet, then read the document title through
JavaScript. Reload should reset the UI counter and scope ID while the next
backend call continues its count. Close should close the desktop window.
Hybrid disposes the old page scope on reattachment and the active page scope
on shutdown. WebAssembly's browser runtime has its own scoped-service lifetime.

Interactive rendering, reload/close behavior, disposal and these debugger
breakpoint locations require a real desktop run; treat them as unverified until
that run has been completed.
