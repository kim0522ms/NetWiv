# C# application contract

NetWiv keeps the official JavaScript package unchanged. The C# namespace is
`NetWiv`; `NetWiv.Platform.Desktop` supplies the native desktop runtime. This document
describes the implemented integration surface, not a claim that every TAURI®
operation has passed conformance testing.

## Naming and ownership

| Tauri concept | C# surface | Contract |
| --- | --- | --- |
| `Builder` | `Builder` | Registers commands, state, plugins, capabilities and windows before building an application. |
| `App` | `App : IDisposable` | Owns the runtime and application lifetime; call `Run` on the UI thread. |
| `AppHandle` | `AppHandle` | Provides application operations to commands and callbacks. Native UI work is dispatched to its owning event loop. |
| `State<T>` | `State<T>` | Retrieves application-managed state; application code owns synchronization of mutable state. |
| `Window`, `Webview`, `WebviewWindow` | Same names | Window and WebView labels occupy separate namespaces. A bare window does not imply a WebView. |
| `#[command]` | `[Command]` and `NetWiv.Generators` | Generates direct static or DI-resolved instance invocation and serialization roots for NativeAOT. |
| `generate_handler!` | `InvokeHandler` | Selects generated command names explicitly. |
| Rust future | `Task` / `ValueTask` | An asynchronous C# operation does not move the UI event loop off its owning thread. |
| `Channel<T>` / resource ID | `Channel`, `Resource` subclasses | Callbacks are bound to a document generation; resource handles have explicit close/dispose lifetimes. |
| Tauri plugin | `IPlugin`, `[Plugin]` | NuGet references and generated static factories root plugins without runtime assembly discovery. |

PascalCase applies to C# names. It does not rename JavaScript command strings,
argument properties, event names, resource IDs or serialized enum tags. Rust
traits, lifetimes, macros and ABI are not binary-compatible with C#; their
corresponding registration and ownership rules are explicit in the SDK.

`App` and `AppHandle` return nullable values from `GetWindow`, `GetWebview` and
`GetWebviewWindow`, corresponding to Rust `Option<T>`. Test the result before
using a label that may have been closed. A window is a `WebviewWindow` only while
it hosts exactly its same-label WebView; a differently named child makes it a
multi-WebView window. An unrelated same-label WebView in another window does not
qualify. `Webview.Window` follows native reparenting for retained SDK handles.
`Windows`,
`Webviews` and `WebviewWindows` return collection snapshots; document/window
closure is reflected in subsequent lookups.

`App` and `AppHandle` expose `Manage<T>`, `State<T>`, `TryState<T>` and
`Unmanage<T>(out T)`. Runtime registration returns false for a duplicate type;
`Builder.Manage` rejects duplicates when building. Registry access is synchronized,
but mutable state values still need application synchronization. `TryState`
returns a nullable `State<T>`; `Unmanage` uses the C# Try/out convention to retain
an unambiguous absent result for both value and reference types.

Successfully managed `IDisposable` objects are disposed when the app is disposed,
including cleanup after failed construction. Removing a value transfers cleanup
responsibility to the caller; a rejected duplicate stays caller-owned. One object
registered under multiple type keys is disposed once. C# cannot enforce Rust
borrow lifetimes or consume arguments: retained `State<T>` references remain CLR
references, and callers must not use disposed state after application shutdown.
Use `IDisposable` for deterministic cleanup; garbage collection is not a Rust
`Drop` replacement. Cleanup attempts all values and aggregates disposal failures.

## Invocation and restart lifetime

Guest `core:resources:close` calls must match the owning WebView and document
generation of managed resources registered through `InvokeContext.AddResource`.
The ownership check and removal are atomic. Trusted backend calls retain access,
and app-scoped resources created through `Resources.Add` remain shareable.

Guest calls to registered C# commands (including generated and managed plugin
commands) execute on the .NET thread pool, including synchronous work before
the first `await`. Backend `InvokeAsync` calls retain caller-thread execution.
Native window operations keep their event-loop dispatch. This NetWiv scheduling
contract applies on Windows, Linux and macOS; it is not a claim of identical Tauri/Rust
command scheduling. It avoids freezing the WebView during synchronous handlers
without requiring every app to wrap every command in `Task.Run`.

Commands may run concurrently. Protect shared state with locks or a semaphore;
never depend on invocation arrival order. Migrate handlers that use thread-affine
native APIs to `AppHandle.Dispatch`, or use the SDK's asynchronous window APIs.
Await I/O with the supplied cancellation token. Backend callers doing CPU-heavy
work must explicitly schedule it away from the UI thread. Avoid `.Result` and
`.Wait()` on native UI operations.

The `CancellationToken` supplied to commands combines caller cancellation with
application shutdown and, for guest calls, the originating WebView document's
lifetime. Reloading or closing that document cancels its pending commands.
Cancellation is cooperative: application code must pass the token to asynchronous
work or observe it explicitly. Already accepted native UI mutations retain their
existing completion contract.

Use `InvokeContext.AddResource(value)` for resources created on behalf of a guest.
The handle is closed when its document is replaced or closed; a late registration
from an obsolete document is rejected and disposes an `IDisposable` value. Trusted
backend registrations remain application-owned. `AppHandle.Resources.Add` keeps
its explicit application lifetime for callers that intentionally need it.

`AppHandle.Restart()` requests normal exit. After the event loop returns,
`App.Run` disposes the old app and plugins before launching the current executable
with its arguments and working directory. Repeated pending requests coalesce;
cleanup failure prevents relaunch. Custom runtime hosts that do not use `App.Run`
must not treat `Restart` as an immediate process launch.

## Backend events

### Initial window configuration

`UseEmbeddedConfig` respects `create: false` and the default configured title
`NetWiv App`. An explicit `app.windows` list replaces previously added windows,
including an empty list. An omitted list preserves explicit C# `Window(...)`
calls, but loading configuration without either source of windows creates no
implicit UI. Windows added after configuration remain explicit additions.
The no-configuration `Builder` convenience default remains separate from this
configuration behavior. Windowless apps can run the native event loop, query
windows/WebViews and exit explicitly.

### Event delivery

Invalid backend event names use Tauri's exact error text and Rust Debug escaping
in `ArgumentException.Message`. Guest event command
deserialization retains its distinct upstream argument-error message.

`App`, `AppHandle`, `Window`, `Webview` and `WebviewWindow` expose `Listen`,
`Once`, `ListenAny`, `OnceAny`, `Emit` and `EmitTo`. `Listen`/`Once` select that
manager's target; `ListenAny`/`OnceAny` receive every target for the named event.
`Emit` broadcasts regardless of which manager sends it. `EmitTo` accepts either
a `RuntimeEventTarget` or a label, which matches that label across target kinds.
These synchronous backend operations share the same event bus as guest delivery.
Existing `EmitAsync` methods on window/WebView wrappers remain available.

Dispose the returned listener subscription to unsubscribe, or pass `AppEvent.Id`
to any manager's `Unlisten`. IDs identify backend listeners across all managers;
unknown IDs are ignored. A once listener is removed before its callback, so a
reentrant emission cannot invoke it again. The underlying `AppHandle.Events`
bus retains its existing global-listener default; `App.Listen` selects App.
The Rust manager emit/emit_to tests preserve all four original emitter types and
exact recipient/payload assertions.

## Minimal explicit-runtime application

Reference `NetWiv.Core`, `NetWiv.Platform.Desktop` and `NetWiv.Generators`, all at
the same version. See the [package quickstart](integration-quickstart.md) for
feed setup. Keep `[STAThread]` on the Windows entry point. On macOS, keep a
synchronous `Main` and call `app.Run()` on the process's original main thread;
`Task.Run` or an asynchronous `Main` continuation cannot own AppKit's event loop.
The example below meets both requirements:

```csharp
using NetWiv;
using NetWiv.Platform.Desktop;
using System.Text.Json.Serialization;

[assembly: CommandJsonContext(typeof(AppJsonContext))]

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var app = new Builder()
            .UseRuntime(new DesktopRuntime())
            .Metadata(new("Greeting", "0.1.0", "dev.example.greeting"))
            .Window("main", "Greeting", "index.html")
            .UseEmbeddedAssets()
            .InvokeHandler("greet")
            .Build();
        app.Run();
    }
}

public static class Commands
{
    [Command("greet")]
    public static string Greet(string name) => $"Hello, {name}";
}

[JsonSerializable(typeof(string))]
public partial class AppJsonContext : JsonSerializerContext;
```

The matching frontend remains:

```js
import { invoke } from '@netwiv/api/core'
const greeting = await invoke('greet', { name: 'NetWiv' })
```

Embed frontend files under the `netwiv.assets.` resource prefix, or use the
`NetWiv.Build` MSBuild targets with `NetWivFrontendDist`. `UseEmbeddedConfig`
consumes the expanded `netwiv.config.json` resource. Typed commands must declare
the source-generated `JsonSerializerContext` through `[CommandJsonContext]`;
see the generator tests and conformance application for an executable example.

To use dependency injection, configuration, options, `ILogger` and hosted
services, build the app with `NetWiv.Hosting` instead; see
[Generic Host integration](hosting.md). `Builder.UseServiceProvider` and
`Builder.UseLoggerFactory` connect an existing provider without the host.

## Security and lifecycle

Without an app command manifest, registered application commands are allowed
from bundled local documents and denied from remote documents. Core and plugin
commands still require capability permissions. With an app command manifest,
every custom command requires a resolved capability permission. Denies take
precedence. Permission allow/deny lists contain exact, case-sensitive command
keys; `*` has no wildcard meaning there. Window and WebView capability target
patterns retain their upstream glob behavior.
`netwiv build` reads application `permissions/**/*.json` and `permissions/**/*.toml`
and embeds their declarations in the effective configuration consumed by
`UseEmbeddedConfig`. Permission JSON5 files are ignored, matching the pinned
loader. A nonempty permission/set/default manifest enables application ACL even
when no C# `AppCommandManifest` call is present; an empty permissions directory
preserves manifest-absent defaults. Sets retain each leaf command's own scopes.
During `netwiv dev`, changes to permission/capability files and configuration
overlays regenerate that embedded ACL and restart the backend. Invalid TOML/JSON
configuration stops the old backend, reports the error, and waits for a corrected
file. The frontend server and initial development hook remain session-scoped;
restart `netwiv dev` when changing their configuration.
Calling private `window.__TAURI_INTERNALS__` directly is outside the
public compatibility guarantee.

`App.AssetProtocolScope()` and `AppHandle.AssetProtocolScope()` return the live
`FileScope` when the file asset protocol is enabled. `AllowFile`, `AllowDirectory`,
`ForbidFile` and `ForbidDirectory` extend it at runtime. Their paths are literal:
a filename containing `*`, `?` or brackets does not become a broad glob grant.
`IsForbidden` and `IsAllowed` preserve deny precedence. Pattern properties return
snapshots. `Listen`/`Once` return an ID for `Unlisten`; scope callbacks may change
the scope again without holding a native/UI lock. A scope change immediately
affects subsequent `asset:` protocol requests.

Plugin `Initialize` registers commands and permissions while building. Plugin
settings are available as `PluginContext.Config`; `ReadConfig(JsonTypeInfo<T>)`
reads a typed value with generated NativeAOT metadata. Missing settings are JSON
null. See [plugin configuration](plugin-configuration.md) for setup and capability
registration. Plugin
`Setup` and application `Setup` execute once the native event loop and initial
windows are ready. Native callbacks never unwind a managed exception through
the C ABI. A callback failure stops the runtime and is rethrown after native
cleanup.

Reload, navigation and WebView closure invalidate old command replies, channels
and guest event listeners. A late reply is discarded instead of being delivered
to a new document that happens to use the same label. Accepted native mutations
complete normally even when caller cancellation arrives later, so newly-created
resource handles are not lost.

`OnMenuEvent` and `OnTrayIconEvent` observe native events; emitting a similarly
named guest event does not invoke these native-only hooks. Menu IDs retain the
application-supplied identifier. Tray positions retain fractional native
coordinates.

`Window.OnCloseRequested` accepts synchronous or asynchronous handlers. Native
closing is deferred while subscribed; the handler may call `PreventDefault`.
After a successful handler completes, the SDK destroys the window unless
prevention was requested. Disposing the subscription prevents future calls
and does not cancel a handler already running. Handler and subsequent destroy
errors are reported through `AppHandle.EventHandlerFailed`; without a subscriber
they are written to standard error. A failed handler does not trigger destroy.

Window event conveniences include resize, move, focus/blur, scale, theme and
drag/drop. The returned `IDisposable` removes all underlying listeners. `Once`
removes its listener before invocation, including concurrent emissions.

`Windows`, `Webviews` and `WebviewWindows` are local lifecycle snapshots.
`GetAllWindowsAsync`, `GetAllWebviewsAsync` and `GetAllWebviewWindowsAsync` query
the native runtime. Their singular async lookups return null when absent;
`GetFocusedWindowAsync` checks current native focus. The pinned guest's combined
window listing wraps all window labels, including bare windows, whereas the
local `WebviewWindows` snapshot contains only known combined objects.

Monitor and cursor queries are available on a Window so the native caller has
an explicit window context. Monitor work areas retain physical coordinates;
cursor coordinates use `PhysicalPosition<double>` to retain fractional values.
Window icons and overlays accept an Image resource, an encoded byte buffer or
a path. See [pinned upstream behavior](pinned-upstream-behavior.md) for the
explicit C# backend differences around colors and WebView parent labels.
Explicit `Resource.CloseAsync` preserves invalid-ID errors on repeated close.
`DisposeAsync` is idempotent after a successful close, following .NET cleanup
conventions without masking errors from an explicit API call.

## Deployment boundary

`RuntimeInvokeResponse.Deserialize<T>(JsonTypeInfo<T>)` corresponds to Tauri's
`InvokeResponseBody::deserialize`. Pass metadata from a generated JSON context;
runtime reflection is not required. JSON values use that context's serializer
contract. Raw responses deserialize as numeric sequences, and `byte[]` uses the
Tauri byte-vector array format (including for JSON responses), not STJ's default
Base64 string format. Decoding raw bytes into `byte[]` returns an independent
array. Invalid target shapes throw `JsonException`.

`UseEmbeddedConfig()` reads `app.security.assetProtocol` for the wry runtime.
It is disabled by default. For example, a scope of
`{"enable":true,"scope":{"allow":["$APPDATA/images/**"],"deny":["$APPDATA/images/private/**"]}}`
allows files below the application's image directory while giving deny patterns
precedence. The unchanged guest `convertFileSrc(path)` produces the corresponding
asset URL. A configured CSP must separately permit that URL for its intended use.

File scope matching uses literal path separators, canonical filesystem paths,
and the pinned platform default for leading dots (Unix requires a literal dot;
Windows does not). These filesystem scopes are separate from command capabilities.
Responses use the native WebView's initial document origin for CORS. The legacy
direct-binding backends reject enabled asset configuration explicitly. Dynamic
scope mutation is exposed through `AppHandle.AssetProtocolScope()` and
`FileScope` allow/forbid methods, snapshots and change events.

Application restore, managed development and managed/NativeAOT publishing use the native
assets already in NuGet. They do not execute Cargo or download a runtime library
at startup. Deployment currently uses the NativeAOT application plus the
platform DLL/SO and its documented OS dependencies. Framework maintainers build
the native adapter separately using the pinned Rust toolchain.

See [native architecture](native-architecture.md) for the supported backend and
[validation boundaries](implementation-status.md) for outstanding coverage.
Isolation has an automatic differential suite; full desktop acceptance remains
unfinished. Consult the relevant conformance results for a specific behavior.

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.
WRY is a trademark of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.
