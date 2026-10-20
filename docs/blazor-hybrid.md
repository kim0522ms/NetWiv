# Blazor Hybrid development and WebAssembly deployment

`NetWiv.Blazor` optionally runs Razor components on managed .NET inside the
desktop process. The browser renders the DOM through Microsoft's Blazor
WebView renderer. This provides a regular managed debugging target for Razor
handlers, codebehind and desktop commands in one process.

The integration currently supports **Windows x64 with WebView2**. Linux and
macOS integration is unverified and is explicitly rejected by this adapter.
The optional package reuses `Microsoft.AspNetCore.Components.WebView` 8.0.32;
it does not implement another Razor renderer or copy the framework JavaScript.
Applications that do not reference `NetWiv.Blazor` do not acquire that dependency.

## Choose the execution mode

The [shared UI sample](../samples/BlazorHybrid/README.md) compiles one Razor
class library into two hosts:

| Mode | Razor execution | Desktop backend | Sample default |
| --- | --- | --- | --- |
| `Hybrid` | Managed .NET in the desktop process | Managed .NET | `Debug` |
| `WebAssembly` | Blazor WebAssembly inside the WebView | Managed .NET or NativeAOT | `Release` and other configurations |

Hybrid **does not support NativeAOT**. The upstream WebView implementation uses
reflection-based serialization and component activation that the public host
adapter cannot replace. The package rejects `PublishAot=true` with an explicit
build error. This exception belongs to the optional Hybrid package; the ordinary
NetWiv desktop runtime and WebAssembly backend retain their NativeAOT paths.

Switching a configuration name alone is insufficient in a custom application.
Its WebAssembly build must exclude the `NetWiv.Blazor` reference, the desktop
reference to the shared Razor assembly, and the Hybrid registration code.
Publish the WebAssembly UI separately, then stage its complete `wwwroot` in the
desktop app. The sample performs these steps conditionally with
`NetWivBlazorMode`. NativeAOT applies to the desktop backend, independently of
the browser's WebAssembly runtime.

From the repository root, after preparing the native artifacts described in
[framework development](development.md):

```powershell
dotnet run --project samples/BlazorHybrid/Desktop -c Debug
dotnet run --project samples/BlazorHybrid/Desktop -c Debug -p:NetWivBlazorMode=WebAssembly
dotnet publish samples/BlazorHybrid/Desktop -c Release -r win-x64 -p:PublishAot=true
```

These are source-sample commands. For package consumers, use matching versions
from the [local feed](integration-quickstart.md), including `NetWiv.Blazor` only
in the Hybrid host. Create the shared UI layout with
`netwiv new MyBlazorApp --template blazor-hybrid --source FEED_DIRECTORY`, then
run `dotnet run --project src-netwiv` from the generated project. The existing
`netwiv new --template blazor` starter remains a WebAssembly starter.

## Register a Hybrid webview

Reference `NetWiv.Blazor`, `NetWiv.Hosting` and `NetWiv.Platform.Desktop` at
matching versions. Register the shared UI's own services separately:

```csharp
using Microsoft.Extensions.DependencyInjection;
using NetWiv.Blazor;
using NetWiv.Hosting;
using NetWiv.Platform.Desktop;

// Run this startup code from an [STAThread] Main method.
var builder = NetWivApplication.CreateBuilder(args);
builder.Services.AddNetWivBlazor();
// Register services injected by AppRoot here.

var options = new BlazorWebViewOptions
{
    HostPage = "wwwroot/index.html",
    WebviewLabel = "main"
};
options.RootComponents.Add<AppRoot>("#app");

builder.Desktop
    .UseBlazor(new DesktopRuntime(), options)
    .Window("main", "My Blazor app", options.AppBaseUri.AbsoluteUri);

using var app = builder.Build();
app.Run();
```

`AppRoot` is the application's `IComponent` implementation, normally a Razor
component in its shared UI library. `UseBlazor` obtains the root provider during
`Build`, after Generic Host has constructed it. A plain `Builder` can instead
use `UseServiceProvider` with a caller-owned provider. Direct adapter usage is
`new BlazorDesktopRuntime(runtime, services, options)`. Prefer the builder
extension for an application: it also registers the private origin with
NetWiv's normal IPC policy.

`WebviewLabel` must identify one existing initial webview. The adapter selects
its bundled URL, leaving other webviews available for ordinary frontend use.
`AppBaseUri` is the fixed browser-facing private Windows origin. It does not
start an HTTP server. A component's forced navigation must remain within that
origin.

`HostPage` resolves relative to `AppContext.BaseDirectory`. Copy the host page
and local assets into the build and publish outputs. Its minimum structure is:

```html
<!doctype html>
<html>
<head><base href="/"><title>My Blazor app</title></head>
<body>
  <div id="app">Loading…</div>
  <script src="_framework/blazor.webview.js"></script>
</body>
</html>
```

The Microsoft package supplies `_framework/blazor.webview.js` and its module
manifest from embedded resources. Do not copy another framework version over
them. Additional static assets can come from a custom `IFileProvider`; when
one is supplied, `HostPage` must be relative to its root and the caller owns
the provider. The default physical provider is owned by the adapter. A file
provider is an application trust boundary: include only files intended for
the frontend.

## Keep shared UI and desktop services separate

The shared sample's Razor handlers use the same generated C# command client and
`IJSRuntime` module in both modes. That module calls `@netwiv/api/core`, so
desktop commands retain NetWiv's command registration, capabilities and
invocation scopes. Direct desktop service injection into a shared Razor
component would make it depend on the Hybrid host and prevent the same UI
from running in WebAssembly.

Hybrid components are trusted application code running with the desktop
process's full .NET access. Command capabilities constrain calls through NetWiv
IPC; they do not sandbox managed components or arbitrary `[JSInvokable]`
methods. Bundle trusted components and frontend assets only.

Keep DTOs in a shared contracts assembly and OS-facing services in the desktop
host. Register browser-compatible UI services in both UI hosts. Hybrid uses
Microsoft's normal scoped `IJSRuntime`, navigation and component services.

The renderer runs on Microsoft's serialized Blazor dispatcher. Native window
operations still use the native runtime's UI dispatcher; component callbacks
must not assume they run on the native UI thread.

## Page lifetime and cleanup

The application owns its root DI provider. A Hybrid page gets its own async
scope; command invocations continue to have their separate NetWiv scopes.
Reload replaces the UI scope while root singletons survive. Document retirement
immediately prevents old renderer messages from reaching a replacement page.
Incoming messages must match the selected webview, actual browser origin and
current document ID. Ordinary NetWiv messages continue through the normal IPC
policy.

Configured application response headers and CSP apply to the Hybrid host page
and assets through Core's shared response policy. The existing isolation
pattern is explicitly unsupported by this integration and is rejected before
starting the native runtime.

The host awaits asynchronous renderer and scope cleanup, with a five-second
shutdown limit. A cleanup failure is reported as an application failure.
Retiring a document completes its outstanding and subsequent JS calls with a
`JSException` indicating disconnection, allowing async component and service
cleanup to continue. This differs from Blazor Server's
`JSDisconnectedException`; cleanup code must allow for `JSException` when the
browser document has gone away. Active-page JS failures and unrelated component
or disposal errors remain application failures. Do not rely on the old DOM or
browser callbacks to release managed resources.

This cancellation adapter recognizes only the pinned Microsoft WebView
transport's JS invocation/completion messages. The dependency uses the exact
NuGet version constraint `[8.0.32]`; a startup check also rejects a substituted
WebView assembly version. Updating that dependency requires reviewing the
upstream private transport and rerunning the retirement, active-error, pending
call and scope-disposal regressions. It does not make the upstream private
wire format a supported application API.

Each page can track up to 4096 JS calls awaiting browser completion. Calls
canceled on the managed side without a browser completion still count until
page retirement. Exceeding the limit fails the application explicitly rather
than retaining an unlimited set of call IDs.

## Debug the managed development host

Build and run `Debug` in `Hybrid` mode with matching source and portable PDBs.
Open `samples/BlazorHybrid/Desktop/Desktop.csproj` as the startup application
in Visual Studio or Rider and use a normal managed .NET launch configuration.
Alternatively, start it with the first command above, then attach the managed
.NET debugger to `NetWiv.BlazorHybrid.Desktop` rather than a WebView2 child
process. Visual Studio provides **Debug → Attach to Process**; Rider provides
**Run → Attach to Process**. See the
[Visual Studio attachment guide](https://learn.microsoft.com/en-us/visualstudio/debugger/attach-to-running-processes-with-the-visual-studio-debugger).

Useful sample breakpoint locations are the Razor `Increment` handler,
`App.razor.cs`'s `GreetAsync`, `GreetingCommands.Greet`,
`GreetingService.CreateGreeting` and the UI/command scope disposal methods.
Inspect the handler argument and local greeting value, then continue to verify
the rendered result. A breakpoint that merely binds does not establish this
complete interaction.

WebView developer tools inspect JavaScript, the DOM and network requests. They
are separate from the managed .NET debugger. WebAssembly mode executes Razor
code in the browser and has a different debugging path. Source breakpoints,
locals, watch expressions and stack inspection were exercised through
NetCoreDbg in a managed Windows desktop run. Visual Studio/Rider UI acceptance,
Hot Reload and cross-platform Hybrid acceptance remain unverified.

## Validation boundary

`tests/NetWiv.Blazor.Tests` exercises the upstream manager's asset routing,
origin checks, page-scope replacement and asynchronous disposal, plus the
runtime adapter's transport and lifecycle behavior. Package-consumer checks
exercise managed dependency consumption and NativeAOT rejection. Automated
checks do not establish rendered interaction or debugger acceptance. A selected
Windows desktop run exercised rendered input, counter updates, command IPC,
JS interop, reload with a new UI scope, surviving backend state, native window
close and the application's close button. Debugger validation is separate from
those UI checks. A packaged NativeAOT desktop consumer using the shared
WebAssembly UI also passed counter updates, command IPC, JS interop, reload
with a new UI scope and surviving backend state, and application close on
Windows. These are selected scenarios and do not establish full desktop or
IDE acceptance.
