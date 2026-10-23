# Blazor Hybrid and WebAssembly applications

`NetWiv.BlazorHybrid` optionally runs Razor components inside the desktop process,
using managed .NET by default. Microsoft's component renderer owns component
state, event dispatch and diffing; the WebView renders its DOM updates.
Managed development provides a regular .NET debugging target for Razor handlers,
codebehind and desktop commands in one process.

The adapter targets **Windows x64 with WebView2**, **Linux x64 with
GTK3/WebKitGTK 4.1** on X11 or Wayland, and **macOS arm64 with WKWebView**.
Use the platform prerequisites in [Linux](linux.md) and [macOS](macos.md).
Platform availability and the validation boundary below are separate claims.
The optional package has `net8.0`, `net9.0` and `net10.0` assets and reuses
the corresponding `Microsoft.AspNetCore.Components.WebView` package. NuGet
selects the asset for the application's target framework:

| Application target | Pinned Microsoft component packages | Hosting |
| --- | --- | --- |
| `net8.0` | `8.0.32` | Managed; experimental NativeAOT opt-in |
| `net9.0` | `9.0.21` | Managed; experimental NativeAOT opt-in |
| `net10.0` | `10.0.13` | Managed; experimental NativeAOT opt-in |

NetWiv does not implement another Razor renderer. For .NET 8/9 it uses
Microsoft's embedded framework assets; for .NET 10 it embeds the unchanged
`blazor.webview.js` and default module manifest from the pinned NuGet package,
which now ships these as static web assets. No runtime download is required.
An optional **experimental NativeAOT host** owns the WebView transport,
generated JSON contracts and typed interop dispatch around that renderer.
It is not a complete NativeAOT implementation of the Blazor API.
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
| `Hybrid` | .NET in the desktop process | Managed .NET | All configurations |
| `Hybrid` with experimental opt-in | Statically registered .NET in the desktop process | Experimental NativeAOT, or managed execution of the same research host | Never automatic |
| `WebAssembly` | Blazor WebAssembly inside the WebView | Managed .NET or NativeAOT | Explicit selection |

Use `AddNetWivBlazorHybrid()` for ordinary applications. It uses Microsoft's
WebView services, standard `DotNetObjectReference<T>` and `[JSInvokable]`
discovery. It does not require the experimental component or callback registry.
This registration requires managed .NET.

`AddNetWivBlazorHybridAot(...)` explicitly selects the experimental host, even
when run on managed .NET. It requires concrete components, generated JSON
metadata and JS-invokable delegates. Unregistered types and callbacks fail
rather than falling back to runtime discovery. Package consumers publishing
NativeAOT must also set `NetWivBlazorExperimentalAot=true`.

The [NativeAOT acceptance tests](../tests/NetWiv.BlazorHybrid.Aot.Tests/README.md)
execute the product adapter, including native WebView rendering and document
replacement. They do not establish compatibility with arbitrary component libraries.
The experimental host checks that the loaded Components, Components.Web,
JSInterop and WebView assemblies match the selected row above. Do not substitute
them through a different `Microsoft.AspNetCore.App` shared-framework reference.
The target framework selects the adapter; runtime roll-forward alone does not
upgrade its Blazor implementation. Use a matching or newer SDK to build an app.
Packing NetWiv itself requires SDK 10 or later to include all three assets.

The sample and generated `blazor-hybrid` starter default to `net8.0`. Set
`NetWivBlazorTargetFramework` in their `Directory.Build.props` to `net9.0` or
`net10.0`, or pass `-p:NetWivBlazorTargetFramework=net10.0` to `dotnet`.
This keeps the desktop, shared Razor UI, binding-generation build and
WebAssembly project on the same framework and Microsoft package versions.

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
dotnet publish samples/BlazorHybrid/Desktop -c Release -r win-x64

dotnet publish samples/BlazorHybrid/Desktop -c Release -r win-x64 -p:NetWivBlazorMode=WebAssembly -p:PublishAot=true

# Experimental: retains the limitations described below.
dotnet publish samples/BlazorHybrid/Desktop -c Release -r win-x64 -p:NetWivBlazorExperimentalAot=true -p:PublishAot=true
```

These publish examples use `win-x64`. Use `linux-x64` on Linux or `osx-arm64`
on macOS and publish on the target OS, with its native compiler/linker tools
when selecting NativeAOT. Linux execution needs a reachable X11 or Wayland
session; `GDK_BACKEND=x11` or `GDK_BACKEND=wayland` selects the display backend.

These are source-sample commands. For package consumers, use matching versions
from the [local feed](integration-quickstart.md), including `NetWiv.BlazorHybrid` only
in the Hybrid host. Create the shared UI layout with
`netwiv new MyBlazorApp --template blazor-hybrid --source FEED_DIRECTORY`, then
run `dotnet run --project src-netwiv` from the generated project. The existing
`netwiv new --template blazor` starter remains a WebAssembly starter.

The CLI selects the same project property with `--blazor-mode`:

```powershell
dotnet tool run netwiv -- dev --blazor-mode Hybrid
dotnet tool run netwiv -- build --no-bundle --blazor-mode Hybrid
dotnet tool run netwiv -- build --no-bundle --aot --blazor-mode WebAssembly
dotnet tool run netwiv -- build --no-bundle --aot --blazor-mode Hybrid --experimental-blazor-aot
```

`--aot` alone does not opt a Hybrid app into experimental hosting.
`--experimental-blazor-aot` requires both `--aot` and explicit
`--blazor-mode Hybrid`; the same option is available for `netwiv dev`.
Explicit AOT development modes use separate `dev-aot-hybrid-experimental` and
`dev-aot-webassembly` directories under `target/<target>/debug/` (or `release/`
with `--release`), so switching modes does not reuse the other UI's assets.
Without `--blazor-mode`, the existing `dev-aot` directory is retained.

### Managed edit loop

The default `netwiv dev` path runs `dotnet watch` with `--no-hot-reload`.
Watched edits rebuild and restart the managed desktop process. In the generated
starter, the watch inputs include the shared Razor UI, contracts, WebAssembly
project and shared assets, including the local `@netwiv/api` modules. The source
sample includes its linked shared assets in the watch list as well. This gives
Razor and desktop C# changes a repeatable managed development loop, but it is a
full process restart: in-memory component and backend state starts over, and
Hot Reload is not part of this path.

For a generated starter, restore its local CLI tool once, then run from the
generated project root:

```powershell
dotnet tool restore
dotnet tool run netwiv -- dev
```

For the repository source sample, run from the repository root:

```powershell
dotnet watch --no-hot-reload --project samples/BlazorHybrid/Desktop/Desktop.csproj run -c Debug
```

Use a normal application close or browser reload when checking Hybrid page and
application cleanup. The adapter's normal shutdown and page-retirement paths
are described in [Page lifetime and cleanup](#page-lifetime-and-cleanup); a
forced debugger stop does not establish that asynchronous disposal completed.

## Register a Hybrid webview

Reference `NetWiv.BlazorHybrid`, `NetWiv.Hosting` and `NetWiv.Platform.Desktop` at
matching versions. Register the shared UI's own services separately:

```csharp
using Microsoft.Extensions.DependencyInjection;
using NetWiv.BlazorHybrid;
using NetWiv.Hosting;
using NetWiv.Platform.Desktop;

// Run this startup code from an [STAThread] Main method.
var builder = NetWivApplication.CreateBuilder(args);
builder.Services.AddNetWivBlazorHybrid();
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
component in its shared UI library. The managed host does not require an
additional registry for every child component or JS-invokable method.
`UseBlazor` obtains the root provider during
`Build`, after Generic Host has constructed it. A plain `Builder` can instead
use `UseServiceProvider` with a caller-owned provider. Direct adapter usage is
`new BlazorDesktopRuntime(runtime, services, options)`. Prefer the builder
extension for an application: it also registers the private origin with
NetWiv's normal IPC policy.

`WebviewLabel` must identify one existing initial webview. The adapter selects
its bundled URL, leaving other webviews available for ordinary frontend use.
`AppBaseUri` is the browser-facing private origin selected for the current
platform. Use this value when creating the Hybrid window rather than copying a
URL from another platform. The adapter does not start an HTTP server. A
component's forced navigation must remain within that origin.

| Platform | `AppBaseUri` |
| --- | --- |
| Windows x64 | `http://netwiv-blazor.localhost/` |
| Linux x64 and macOS arm64 | `netwiv-blazor://localhost/` |

The Windows HTTP-shaped origin is Wry's mapping for the same private custom
protocol. The native scheme is registered on Linux and macOS before the
webview is created. An application must use the selected URI for navigation,
resource URLs and origin comparisons.

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

For the default managed host, use `Microsoft.NET.Sdk.Razor` in the desktop
project, as the starter does. The Razor SDK creates the entry assembly's static
web asset manifest for development and stages referenced library assets under
`wwwroot/_content` for publication. Microsoft's managed WebView manager reads
that development manifest automatically. A plain `Microsoft.NET.Sdk` desktop
project does not consolidate the referenced library asset graph.

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

For a managed instance callback, use the standard `DotNetObjectReference<T>`
and `[JSInvokable]` method. Dispose the reference with its owning component or
service. The shared sample uses this same reference implementation in managed
Hybrid and WebAssembly.

For an **experimental AOT** instance callback, register a typed delegate with
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
In the **experimental host**, `Router` assembly discovery, `NavigationLock` and
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

The Hybrid bridge retires its transport when the host document receives
`pagehide`. If the browser restores that document from its back/forward cache,
`pageshow.persisted` triggers a full reload. Normal page attachment then creates
a fresh UI scope and resets component state while backend singletons survive.
This policy uses the reload path rather than restoring the old renderer.
Blazor's internal routing retains the current document and scope. Core IPC
transport restoration is a separate behavior.

Linux authenticates the bridge against the current native main document before
the adapter receives messages. Its native current custom-scheme URI supplies
source authority, anchored to the committed origin, even when JavaScript reports
an opaque origin. Provisional navigation retains the current document's binding
and source snapshot so pending renderer and interop messages can complete.
Committed replacement and close retire the binding. This transport is private;
application commands continue to use `@netwiv/api`. See [native IPC](native-abi.md).

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

The default managed `AddNetWivBlazorHybrid` path also completes pending and future
JS calls with `JSDisconnectedException` on retirement. Components can catch this
exception around browser cleanup and then continue releasing managed resources;
this includes module calls and disposal. Previously this path emitted a marker
`JSException`; remove marker-string checks and catch `JSDisconnectedException`.
Only exceptions created by the retiring page are treated as expected by the host.

The managed adapter recognizes the pinned WebView transport's JS messages and
uses private JSRuntime pending-task cleanup and completion members to retain the
typed exception. Microsoft's wire failure completion only produces `JSException`.
The completion helper is resolved from the already-loaded Microsoft assembly;
this managed-only adapter is not used by the experimental NativeAOT host.
The dependency uses the exact
NuGet version constraint for each target in the table above; a startup check also rejects a substituted
WebView or JSInterop assembly version. Updating that dependency requires reviewing
the upstream private transport and task-completion members and rerunning the retirement, active-error, pending
call and scope-disposal regressions. It does not make the upstream private
wire format a supported application API.

The experimental host limits each page to 4096 pending JS calls,
4096 bound object references and 4096 active inbound callbacks. Cancellation
immediately removes a pending JS call; dispose object-reference leases promptly.
Retirement stops waiting for inbound application callbacks and discards their
late results, while observing eventual failures. It cannot cancel the callback's
own database, network or other work: application services must cancel that work
when their page scope is disposed. The managed cancellation
adapter retains canceled browser call IDs until completion or page retirement,
with a separate 4096-ID limit. Both paths reject excess calls rather than
retaining an unlimited set of IDs.

## Experimental NativeAOT limits

These restrictions belong to `AddNetWivBlazorHybridAot`, including managed
execution of that host. They are not requirements imposed on the default
managed host. Broader NativeAOT support and library forks are deferred future
work, outside the current delivery; see the
[source audit and future work](blazor-hybrid-aot-research.md).

Register every concrete component, including closed generic framework children
and every possible `DynamicComponent` target, in `BlazorAotOptions.Components`.
A constructor factory can use `Register<TComponent>(Func<IServiceProvider,
TComponent>)`. Supply source-generated `JsonSerializerContext` metadata through
`JsonTypeInfoResolver` and register callback delegates separately. There is no
component-registration source generator; the application owns these lists.

Static registration covers concrete components, JSON metadata and invokable
delegates. It does not make arbitrary Razor libraries AOT-compatible.
The adapter supports built-in DOM event payloads, ordinary JS object references
and byte arrays. Custom DOM event payloads, streaming interop and Microsoft's
`DotNetObjectReference<T>` are unsupported; use the owned reference API described
above for instance callbacks.
In the pinned ASP.NET Core 8.0.32, `compositionstart`, `compositionupdate` and
`compositionend` require custom event registration and are unsupported by this
experimental host.

Keep upstream trimming and AOT diagnostics visible. The pinned component
framework emits `IL2072` warnings for component metadata obtained through
`object.GetType()`. Native publication also reports `IL3050` in JSInterop
serializer and object-reference converter paths. The form sample includes
additional `IL3050` paths in
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
Use an SDK supported by the selected IDE. The generated solution does not pin
an SDK; an ancestor `global.json` and installed SDKs can select a newer SDK than
the IDE's MSBuild supports, even when the application targets .NET 8. If project
loading reports that mismatch, select an installed compatible SDK in the app's
own `global.json`, or use a compatible IDE. This is separate from selecting
`NetWivBlazorTargetFramework`; see Microsoft's
[SDK and Visual Studio version requirements](https://learn.microsoft.com/dotnet/core/porting/versioning-sdk-msbuild-vs).

Open `samples/BlazorHybrid/Desktop/Desktop.csproj` as the startup application
in Visual Studio or Rider and use a normal managed .NET launch configuration.
Alternatively, start it with the first command above, then attach the managed
.NET debugger to `NetWiv.BlazorHybrid.Desktop` rather than a WebView child
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
NetCoreDbg in a managed Windows desktop run. Under Linux WSLg, a real DOM click
hit a source breakpoint; stepping changed the component counter from 0 to 1,
and both the application and debugger exited normally. Visual Studio/Rider UI
acceptance and Hot Reload remain unverified. On macOS arm64,
NetCoreDbg also bound a Razor source breakpoint, read the component's counter,
stepped and resumed the application to normal exit. NetCoreDbg teardown crashed
after the debuggee exited; successful application debugging does not resolve
that debugger limitation.

## Validation boundary

The .NET 8/9/10 targets have matching-package checks and managed transport/lifecycle
regressions. Windows x64 native WebView checks exercise both managed and experimental
AOT hosts across these targets, including events, DTO/callback round trips, reload,
history return and asynchronous scope cleanup. The .NET 10 checks additionally
exercise global/object JS property access and constructor calls. The experimental
host advertises interactive `WebView` renderer metadata on .NET 9/10.
Linux WSLg X11 and native Wayland checks also pass for the .NET 9/10 managed and AOT targets.
Physical macOS arm64 WKWebView checks pass for all three targets in both managed
and AOT modes, including registered-scheme IPC, reload, history return and
asynchronous scope cleanup. The debugger checks described above used the .NET 8
target; they do not establish .NET 9/10 debugger acceptance.

`tests/NetWiv.BlazorHybrid.Tests` covers asset routing, registration, origin checks
and the managed adapter's page replacement, transport and asynchronous cleanup.
`tests/NetWiv.BlazorHybrid.Aot.Tests` separately executes the product's registered
host and typed interop with reflection JSON disabled. Its protocol tests include
malformed messages, stale documents, bounded queues and retirement during a
pending application callback. Its native WebView mode exercises actual Razor
DOM events, interop, browser reload and disposal of both page scopes.

On Windows, the default managed sample passed counter input, invalid/valid form
submission, navigation, command IPC, JS interop and nested DTO callbacks using
standard `DotNetObjectReference<T>`, followed by reload with a new UI scope and
normal process exit. The experimental static host separately passed selected
interactions in managed and NativeAOT execution. An installed CLI created
a fresh NuGet consumer and published it with
the experimental Hybrid AOT build path; that executable passed the same
interactions, reload with a new UI scope and surviving backend state, and
application close. Managed source breakpoints, locals, watch expressions,
stack inspection and continuation were exercised with NetCoreDbg separately.
The shared WebAssembly UI also has selected packaged NativeAOT desktop checks.
These are selected scenarios, not full Razor-library or IDE acceptance.

The managed-only Razor class library fixture additionally exercises assembly
route discovery, data-annotation `EditForm` validation, a library `_content`
JavaScript module and asynchronous component/module/scope disposal after reload
and close. These checks passed on Windows x64 for .NET 8, 9 and 10. The fixture
uses the Razor SDK's asset graph and is excluded from experimental NativeAOT
publication. It does not establish arbitrary component-library acceptance.
An installed CLI also generated a fresh .NET 8 consumer referencing that library
as a NuGet package. Its Debug output and managed publish both passed the selected
route, invalid/valid form and module checks through NetWiv's asset provider,
followed by normal close and component/module/scope cleanup. No test-only asset
copy target was used.

The portable source harness covers native rendering, input events,
DTO/object-reference interop, document replacement and async scope cleanup in
the ordinary Microsoft managed host and experimental NativeAOT host. These
selected scenarios passed on physical Windows x64 and macOS arm64, and on Linux
x64 under WSLg with X11 and native Wayland. Its application-builder path also
checks actual command IPC, protocol and postMessage transport, foreign-frame
rejection and private-header filtering. Package-only acceptance is checked
separately by the [release consumers](release-packages.md); source-host success
alone does not establish it. Physical Linux, minimum-macOS, IDE debugger and
arbitrary component-library acceptance remain separate validation boundaries.
