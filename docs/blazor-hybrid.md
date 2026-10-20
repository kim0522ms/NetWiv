# Blazor Hybrid and WebAssembly applications

`NetWiv.BlazorHybrid` optionally runs Razor components inside the desktop process,
using managed .NET or NativeAOT. Microsoft's component renderer owns component
state, event dispatch and diffing; the WebView renders its DOM updates.
Managed development provides a regular .NET debugging target for Razor handlers,
codebehind and desktop commands in one process.

The integration currently supports **Windows x64 with WebView2**. Linux and
macOS integration is unverified and is explicitly rejected by this adapter.
The optional package reuses `Microsoft.AspNetCore.Components.WebView` 8.0.32;
it does not implement another Razor renderer or copy the framework JavaScript.
The statically registered host owns the WebView transport, generated JSON
contracts and typed interop dispatch around that renderer.
Applications that do not reference `NetWiv.BlazorHybrid` do not acquire that dependency.

### Rename from earlier development builds

Earlier development builds used `NetWiv.Blazor`. Update the package/project
reference and `using` directives to `NetWiv.BlazorHybrid` (including `.Aot`
subnamespaces). Replace `AddNetWivBlazor()` with `AddNetWivBlazorHybrid()` and
`AddNetWivBlazorAot(...)` with `AddNetWivBlazorHybridAot(...)`. The assembly and
transitive targets now also use `NetWiv.BlazorHybrid`. There is no forwarding
package or old-name API alias. `UseBlazor`, the options types and the
`blazor-hybrid` CLI template name are unchanged.

## Choose the execution mode

The [shared UI sample](../samples/BlazorHybrid/README.md) compiles one Razor
class library into two hosts:

| Mode | Razor execution | Desktop backend | Sample default |
| --- | --- | --- | --- |
| `Hybrid` | .NET in the desktop process | Managed .NET or NativeAOT | `Debug` |
| `WebAssembly` | Blazor WebAssembly inside the WebView | Managed .NET or NativeAOT | `Release` and other configurations |

Use `AddNetWivBlazorHybridAot` for Hybrid applications that share one registration path
between managed development and NativeAOT deployment. Register concrete
components, generated JSON metadata and JS-invokable delegates explicitly.
Unregistered components, DTO types and callbacks fail rather than falling back
to reflection JSON or assembly discovery. The existing `AddNetWivBlazorHybrid`
registration remains a managed-only integration.

The [NativeAOT acceptance tests](../tests/NetWiv.BlazorHybrid.Aot.Tests/README.md)
execute the product adapter, including real WebView2 rendering and document
replacement. They do not establish compatibility with arbitrary component libraries.
The statically registered host checks that the loaded Components, Components.Web,
JSInterop and WebView assemblies are all 8.0.32. Do not combine it with a newer
`Microsoft.AspNetCore.App` shared-framework reference; the .NET host runtime
version and these pinned package versions are separate.

Switching a configuration name alone is insufficient in a custom application.
Its WebAssembly build must exclude the `NetWiv.BlazorHybrid` reference, the desktop
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

dotnet publish samples/BlazorHybrid/Desktop -c Release -r win-x64 -p:NetWivBlazorMode=Hybrid -p:PublishAot=true
```

These are source-sample commands. For package consumers, use matching versions
from the [local feed](integration-quickstart.md), including `NetWiv.BlazorHybrid` only
in the Hybrid host. Create the shared UI layout with
`netwiv new MyBlazorApp --template blazor-hybrid --source FEED_DIRECTORY`, then
run `dotnet run --project src-netwiv` from the generated project. The existing
`netwiv new --template blazor` starter remains a WebAssembly starter.

The CLI selects the same project property with `--blazor-mode`:

```powershell
dotnet tool run netwiv -- dev --blazor-mode Hybrid
dotnet tool run netwiv -- build --no-bundle --aot --blazor-mode Hybrid
dotnet tool run netwiv -- build --no-bundle --aot --blazor-mode WebAssembly
```

The option does not change the template defaults. `--aot` selects native
compilation independently of the Razor execution mode.
Explicit AOT development modes use separate `dev-aot-hybrid` and
`dev-aot-webassembly` directories under `target/<target>/debug/` (or `release/`
with `--release`), so switching modes does not reuse the other UI's assets.
Without `--blazor-mode`, the existing `dev-aot` directory is retained.

## Register a Hybrid webview

Reference `NetWiv.BlazorHybrid`, `NetWiv.Hosting` and `NetWiv.Platform.Desktop` at
matching versions. Register the shared UI's own services separately:

```csharp
using Microsoft.Extensions.DependencyInjection;
using NetWiv.BlazorHybrid;
using NetWiv.BlazorHybrid.Aot;
using NetWiv.Hosting;
using NetWiv.Platform.Desktop;

// Run this startup code from an [STAThread] Main method.
var builder = NetWivApplication.CreateBuilder(args);
builder.Services.AddNetWivBlazorHybridAot(registration =>
{
    registration.Components.Register<AppRoot>();
    registration.JsonTypeInfoResolver = AppJsonContext.Default;
});
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
component in its shared UI library. `AppJsonContext` is its source-generated
`JsonSerializerContext` for interop DTOs. Register every concrete child component
as well, including closed generic framework components used by forms or
cascading values and every possible `DynamicComponent` target. There is no
component-registration source generator; the application owns this list.
A component with constructor dependencies can use the
`Register<TComponent>(Func<IServiceProvider, TComponent>)` overload.
`UseBlazor` obtains the root provider during
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
the frontend. The statically registered host does not discover Debug
`.staticwebassets.runtime.json` manifests. Copy Razor class library `_content`
assets into the output explicitly, as the shared sample does.

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
host. Register browser-compatible UI services in both UI hosts. The statically
registered Hybrid host supplies page-scoped `IJSRuntime`, navigation and
component services.

For an instance callback, register a typed delegate with
`registration.Interop.RegisterInstance<TInstance, TArgument, TResult>` and the
argument/result `JsonTypeInfo` values. Create an owned
`BlazorAotObjectReference<TInstance>` lease and pass it to JavaScript. Dispose
the lease when its component or service is disposed. The shared sample wraps
this lease behind a small factory so WebAssembly can use its ordinary
`DotNetObjectReference` without referencing the Hybrid package.

The sample demonstrates `@bind`, manual `EditContext` validation,
`NavigationManager` with statically selected views, generated nested DTOs and
an instance callback. Data-annotation validation and routed-assembly discovery
are separate behaviors and are not supplied by those examples.
`NavigationManager.NavigateTo`, `LocationChanged` and refresh are supported.
`Router` assembly discovery, `NavigationLock` and
`RegisterLocationChangingHandler` are unsupported. Enabling a navigation lock
throws because the pinned navigation API does not expose the disconnect
cancellation hook needed by this host. Do not catch that error and retry
registration; this host never invokes location-changing handlers.

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
With `AddNetWivBlazorHybridAot`, retiring a document completes its outstanding and
subsequent JS calls with `JSDisconnectedException`, allowing async component
and service cleanup to continue. Active-page JS failures and unrelated component
or disposal errors remain application failures. Do not rely on the old DOM or
browser callbacks to release managed resources.

The older managed-only `AddNetWivBlazorHybrid` path uses a cancellation adapter whose
disconnection failures are marker `JSException` instances. It recognizes the
pinned Microsoft WebView transport's JS invocation/completion messages.
The dependency uses the exact
NuGet version constraint `[8.0.32]`; a startup check also rejects a substituted
WebView assembly version. Updating that dependency requires reviewing the
upstream private transport and rerunning the retirement, active-error, pending
call and scope-disposal regressions. It does not make the upstream private
wire format a supported application API.

The statically registered host limits each page to 4096 pending JS calls,
4096 bound object references and 4096 active inbound callbacks. Cancellation
immediately removes a pending JS call; dispose object-reference leases promptly.
Retirement stops waiting for inbound application callbacks and discards their
late results, while observing eventual failures. It cannot cancel the callback's
own database, network or other work: application services must cancel that work
when their page scope is disposed. The older managed cancellation
adapter retains canceled browser call IDs until completion or page retirement,
with a separate 4096-ID limit. Both paths reject excess calls rather than
retaining an unlimited set of IDs.

## NativeAOT limits

Static registration covers concrete components, JSON metadata and invokable
delegates. It does not make arbitrary Razor libraries AOT-compatible.
The adapter supports built-in DOM event payloads, ordinary JS object references
and byte arrays. Custom DOM event payloads, streaming interop and Microsoft's
`DotNetObjectReference<T>` are unsupported; use the owned reference API described
above for instance callbacks.

Keep upstream trimming and AOT diagnostics visible. The pinned component
framework emits `IL2072` warnings for component metadata obtained through
`object.GetType()`. The form sample also brings in `IL3050` paths in
`BindConverter` and `System.Linq.Expressions`; normal publishing can summarize
these as `IL2104`/`IL3053`. Use `-p:TrimmerSingleWarn=false` to inspect individual
diagnostics.

The native tests exercise text, checkbox and select binding, and the sample
executes `InputText` with a simple property expression and manual `EditContext`
validation. Those exercised paths work in the native executable. Enum or array
binding, custom type converters, data-annotation validation and more complex
form expressions require separate validation and may need additional static
implementations. Component registration alone does not resolve these warnings.

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

`tests/NetWiv.BlazorHybrid.Tests` covers asset routing, registration, origin checks
and the managed adapter's page replacement, transport and asynchronous cleanup.
`tests/NetWiv.BlazorHybrid.Aot.Tests` separately executes the product's registered
host and typed interop with reflection JSON disabled. Its protocol tests include
malformed messages, stale documents, bounded queues and retirement during a
pending application callback. Its native WebView2 mode exercises actual Razor
DOM events, interop, browser reload and disposal of both page scopes.

On Windows, the shared sample passed counter input, invalid/valid form
submission, navigation, command IPC, module interop and nested DTO instance
callbacks in both managed Hybrid and NativeAOT Hybrid. An installed CLI created
a fresh NuGet consumer and published it with
`build --no-bundle --aot --blazor-mode Hybrid`; that executable passed the same
interactions, reload with a new UI scope and surviving backend state, and
application close. Managed source breakpoints, locals, watch expressions,
stack inspection and continuation were exercised with NetCoreDbg separately.
The shared WebAssembly UI also has selected packaged NativeAOT desktop checks.
These are selected scenarios, not full Razor-library, IDE or cross-platform
acceptance.
