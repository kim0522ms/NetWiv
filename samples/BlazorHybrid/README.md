# Shared Blazor UI: Hybrid and WebAssembly

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
not to the browser UI. Its project graph excludes `NetWiv.BlazorHybrid` and
`Microsoft.AspNetCore.Components.WebView`.

Hybrid can also publish the shared Razor UI into the native desktop executable:

```powershell
dotnet publish samples/BlazorHybrid/Desktop -c Release -r win-x64 -p:NetWivBlazorMode=Hybrid -p:PublishAot=true
```

Hybrid uses `AddNetWivBlazorHybridAot` in managed Debug and NativeAOT builds.
`Desktop/HybridUiRegistration.cs` registers every concrete component, including
the closed `CascadingValue<EditContext>` child used by the form. It also supplies
generated JSON metadata and the instance callback registration. New components
and interop DTOs require corresponding registrations. JSON reflection is disabled.
There is no registration source generator. Copy additional Razor library
`_content` assets explicitly; this host does not discover Debug static-web-asset
manifests.

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

Try Increment, edit the name and Greet, then submit an empty and a filled form.
The form uses an explicit validation message store without reflection-based
data annotations. Details and Home update the local route with
`NavigationManager`; the sample does not discover routed assemblies.
`Router`, `NavigationLock` and location-changing guards are unsupported by the
statically registered Hybrid host.

Roundtrip sends a nested DTO to JavaScript and calls the registered instance
method back in .NET. Its `long` value uses a string JSON contract so the browser
preserves its precision. Hybrid owns an explicit `BlazorAotObjectReference`
lease; WebAssembly uses `DotNetObjectReference` behind the shared factory.
Both are disposed by the page component.

Read the document title through JavaScript. Reload should reset the UI counter and scope ID while the next
backend call continues its count. Close should close the desktop window.
Hybrid disposes the old page scope on reattachment and the active page scope
on shutdown. WebAssembly's browser runtime has its own scoped-service lifetime.

Selected Windows runs exercised managed and NativeAOT Hybrid rendering,
forms, navigation, interop, reload and close. Managed Razor/codebehind
breakpoints, locals and continuation were checked with NetCoreDbg; Rider and
Visual Studio UI acceptance remain unverified. These checks do not establish
arbitrary Razor-library compatibility. See the
[validation boundary and AOT limits](../../docs/blazor-hybrid.md).
