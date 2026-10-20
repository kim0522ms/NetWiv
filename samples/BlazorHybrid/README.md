# Shared Blazor UI: Hybrid and WebAssembly

This source sample compiles the same `Shared/App.razor`, its codebehind and
generated typed command client into two hosts. Both call `greet` through
`wwwroot/netwiv.js` → `@netwiv/api/core.invokeJson` → NetWiv IPC. The UI never
receives a desktop backend service directly.

Both Debug and Release default to **managed Hybrid** on Windows x64/WebView2.
The default host uses `AddNetWivBlazorHybrid` and Microsoft's ordinary
`DotNetObjectReference` through the shared reference factory. Application
components and callbacks do not need the experimental static registry.

Run these commands from the repository root, with the repository's native
Windows artifacts already built as described in `docs/development.md`:

```powershell
dotnet run --project samples/BlazorHybrid/Desktop -c Debug
dotnet run --project samples/BlazorHybrid/Desktop -c Release
dotnet publish samples/BlazorHybrid/Desktop -c Release -r win-x64 --self-contained true -p:PublishAot=false
```

Choose WebAssembly explicitly to run the shared Razor UI in the browser:

```powershell
dotnet run --project samples/BlazorHybrid/Desktop -c Debug -p:NetWivBlazorMode=WebAssembly
dotnet publish samples/BlazorHybrid/Desktop -c Release -r win-x64 -p:NetWivBlazorMode=WebAssembly -p:PublishAot=true
```

The WebAssembly build publishes the browser frontend before copying its
`wwwroot` into the desktop output. NativeAOT applies to the desktop backend,
not to the browser UI. This graph excludes `NetWiv.BlazorHybrid` and
`Microsoft.AspNetCore.Components.WebView`.

Hybrid NativeAOT is a separate **experimental** path. A Hybrid publish with
`PublishAot=true` fails unless `NetWivBlazorExperimentalAot=true` is explicit:

```powershell
dotnet publish samples/BlazorHybrid/Desktop -c Release -r win-x64 -p:NetWivBlazorMode=Hybrid -p:PublishAot=true -p:NetWivBlazorExperimentalAot=true
```

The experimental flag selects `AddNetWivBlazorHybridAot`, disables JSON
reflection and compiles `Desktop/HybridUiRegistration.cs`. It registers every
concrete component, closed generic child, generated JSON contract and instance
callback. It uses an owned `BlazorAotObjectReference` lease. There is no registry
source generator. Router assembly discovery, NavigationLock and location-changing
guards are unsupported on this path. Explicitly copy additional Razor library
`_content` assets; the experimental host does not discover Debug static-web-asset
manifests. This path is not a general substitute for the managed Blazor host or
a guarantee that an arbitrary Razor library supports NativeAOT.

Desktop build and restore outputs are separated by host flavor:
`bin/Hybrid/`, `bin/HybridExperimentalAot/` and `bin/WebAssembly/`, with matching
`obj/` roots. CLI artifact directories also include the flavor. Switching
modes does not reuse the other host's frontend payload.

Attach a managed .NET debugger to the default Hybrid desktop process. Useful
breakpoints are `Shared/App.razor` (`Increment`), `Shared/App.razor.cs`
(`GreetAsync`), `Desktop/GreetingCommands.cs` and `Shared/UiSession.cs` (`Dispose`).
Hybrid runs Razor handlers and the backend in one managed process; WebAssembly
executes Razor handlers in the WebView.

Try Increment, edit the name and Greet, then submit an empty and a filled form.
The form uses an explicit validation message store. Details and Home update the
local route through `NavigationManager`. Roundtrip sends a nested DTO through
JavaScript and invokes the page's instance callback. Its `long` field uses a
string JSON contract so the browser preserves precision. The page disposes its
reference lease in every mode.

Read the document title through JavaScript. Reload should reset the counter and
UI scope ID while the next backend call continues its count. Close should close
the desktop window. Hybrid disposes page scopes on reattachment and shutdown;
WebAssembly's browser runtime has its own scoped-service lifetime.

Selected Windows checks covered the default managed host's forms, navigation,
standard object-reference callbacks, DTO interop, reload and normal process exit.
The static host was checked separately in managed and NativeAOT execution.
Treat the default managed host and the experimental host as separate validation paths. NetCoreDbg handler
checks do not establish Rider or Visual Studio UI acceptance, full Razor-library
compatibility, Hot Reload or another platform's support. See the
[validation boundary and experimental limits](../../docs/blazor-hybrid.md).
