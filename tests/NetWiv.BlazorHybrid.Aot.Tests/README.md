# Blazor Hybrid native product tests

This executable uses the product `NetWiv.BlazorHybrid` package sources. It contains no
copy of the rendering host or interop runtime. The project explicitly opts into
`NetWivBlazorExperimentalAot`; passing it does not establish full Blazor AOT support. Microsoft's unchanged `Renderer`
still owns component state, event dispatch and diffing.

The default mode tests the experimental static host with JSON reflection and
dynamic code disabled. `--managed` runs that same static host under the managed
runtime for development. `--managed-host --native-webview` instead tests the
default Microsoft managed host, with dynamic code and JSON reflection enabled.

## Native execution

Publishing alone does not pass the execution gate:

The default target is `net8.0`. Add `-p:NetWivTestFramework=net9.0` or
`-p:NetWivTestFramework=net10.0` to publish the matching adapter and Microsoft
packages. Each executable verifies the selected package identity. Run both
protocol and native-WebView modes for every target; .NET 10 native checks also
exercise constructor and property get/set operations on global and object targets.

```powershell
dotnet publish tests/NetWiv.BlazorHybrid.Aot.Tests -c Release -r win-x64 -o artifacts/blazor-aot-tests
& ./artifacts/blazor-aot-tests/NetWiv.BlazorHybrid.Aot.Tests.exe
& ./artifacts/blazor-aot-tests/NetWiv.BlazorHybrid.Aot.Tests.exe --native-webview --back-forward
```

The first invocation injects protocol messages and inspects actual serialized
render batches from the product session. It also wraps a controlled native
boundary in the production `BlazorDesktopRuntime` to test origin, webview label,
document identity, reload retirement and closed-document rejection.

The second uses the production `Builder.UseBlazor` path, the native WebView,
packaged Microsoft `blazor.webview.js` and real DOM events. It
reloads the browser document, repeats its assertions and closes the window.
`--back-forward` additionally returns through browser history and checks a fresh
native document, component state and UI scope. WebKit cached returns use the
Hybrid bridge's full reload policy; a normal WebView2 history load is also valid.
All three UI scopes must finish asynchronous disposal. This does not preserve
the old cached renderer or its component state.
It requires the platform WebView and the native backend artifacts described in
[development](../../docs/development.md). Browser assertions are automated;
they are separate from manual visual and IDE debugger acceptance.

For managed development checks:

```powershell
dotnet run --project tests/NetWiv.BlazorHybrid.Aot.Tests -c Release -p:PublishAot=false -- --managed
dotnet run --project tests/NetWiv.BlazorHybrid.Aot.Tests -c Release -p:PublishAot=false -- --managed --native-webview
```

For the default Microsoft managed host:

```powershell
dotnet run --project tests/NetWiv.BlazorHybrid.Aot.Tests -c Release -p:PublishAot=false -p:JsonSerializerIsReflectionEnabledByDefault=true -- --managed-host --native-webview --back-forward
```

On Linux and macOS, publish with `-r linux-x64` or `-r osx-arm64` and run
`./artifacts/blazor-aot-tests/NetWiv.BlazorHybrid.Aot.Tests` with the same arguments.
Linux requires a running X11 or Wayland desktop; select `GDK_BACKEND=x11` or
`GDK_BACKEND=wayland` and the compositor's runtime directory as appropriate.
WSLg execution is separate from physical Linux device acceptance.

Linux also has `--transport-navigation`, run with the managed executable. It
checks ordinary Core IPC after WebKit restores the same cached JavaScript
context: each document must obtain a fresh native grant and retain the correct
source URI. This is separate from Hybrid's reload policy. CI and package checks
run it after the managed native browser assertions, without another publish.

Linux `--hostless-ipc` checks that `about:blank` reaches the managed callback
with its exact native source and current document epoch, in managed and AOT
execution. Rust unit tests additionally reject cross-document hostless URLs.
This does not establish browser IPC support for `data:` URLs: some forms fail
in the pinned Wry request-URI parser before NetWiv receives the message.

`--registered-ipc` runs on all three platforms in managed or NativeAOT
executables. It loads a document through an application-registered `ipc` scheme,
checks custom GET/POST handling and message-based Core commands, and verifies
that unhandled requests cannot fall through to built-in command dispatch.
Private transport headers must not reach the custom handler.

## Assertions

- Compiled Razor components, explicit component registration, injected scoped
  services, direct parameters and a registered `CascadingValue<string>` child.
- Counter click and real text, checkbox and select `@bind` change events.
- Standard `IJSRuntime.InvokeVoidAsync`, module import, module method targeting
  and module disposal. The void fixture returns a cyclic object, which must not
  be serialized when the standard void wire result is requested.
- Generated DTO metadata, nested arrays, nullable values and exact `long`
  endpoints. The fixture uses explicit JSON strings for `long` values to avoid
  JavaScript number precision loss.
- Explicitly registered static and instance `[JSInvokable]` methods returning
  `Task<DTO>`, `ValueTask<DTO>` and `ValueTask<int>`. The attribute alone does not
  register a callback; registration uses the product's typed delegate API.
- Document-owned `BlazorAotObjectReference<T>` rejection after .NET or JavaScript
  disposal. This is an explicit product API rather than automatic support for
  Microsoft's `DotNetObjectReference<T>`.
- Missing metadata and unknown methods fail closed; cancellation, disconnect,
  sender failure and asynchronous invokable exceptions terminate correctly.
- Failed or canceled argument serialization emits no partial byte-array
  packets. Concurrent successful calls retain their own byte-array groups.
- Retirement completes without waiting for an application's pending inbound
  callback; eventual failures are observed and late results are discarded.
  Malformed current-document messages cause controlled failure and scope cleanup.
- A stalled browser cannot retain more than the configured product bound of
  256 unacknowledged render batches; renderer teardown still completes.
- Reload cancels old document JS calls and creates a separate page scope. Real
  browser reload and final process return must dispose both asynchronous scopes.
- `--back-forward` checks a fresh Hybrid scope and usable command/interop paths
  after history return. Linux also checks renderer and command liveness after
  canceled provisional navigation.
- A foreign `data:` iframe attempts raw native messages and forged transport
  envelopes. It must never be attributed to the application document. Linux
  rejects these before the managed boundary; other platforms retain their native
  authenticated sender URL.
- Actual NetWiv application commands execute through both the custom IPC protocol
  and JSON message transport on both documents. Private transport capability and
  epoch headers must be absent from managed asset and command request headers.
  On Linux, modified serialization and header-iterator prototypes must not
  observe the private credentials during these calls.
- The managed-host mode uses Microsoft's `DotNetObjectReference<T>` and ordinary
  attributed callbacks. The experimental host keeps its explicit typed registry.

Protocol checks do not prove DOM behavior. Execute both modes on the native
artifact before claiming NativeAOT browser acceptance. Arbitrary Razor libraries,
streams, restoration of the old cached renderer and navigation locks are outside these tests.

## Trimming and provenance

There are no framework-wide trimming roots or fallback reflection JSON resolver.
Only explicitly registered concrete components preserve their metadata. The
pinned Microsoft Components assembly can emit `IL2072` diagnostics because its
property injection, parameter and cascading paths obtain types from
`object.GetType()`. Native publication also reports `IL3050` diagnostics in
JSInterop serializer and object-reference converter paths. Keep these diagnostics
visible and verify the registered component paths in the native executable.
The experimental checks assert that dynamic code and JSON reflection remain
disabled at runtime; passing them does not establish arbitrary runtime-discovered
component or interop support.
The full form sample has additional upstream AOT diagnostics and a narrower
tested binding contract; see [NativeAOT limits](../../docs/blazor-hybrid.md#experimental-nativeaot-limits).

Microsoft packages are pinned to `8.0.32`, `9.0.21` or `10.0.13`, according to
the consumer's target. Known upstream `IL2072`/`IL3050` diagnostics are allowed
as visible warnings only in this experimental test project; other warnings
remain errors. Adapted product source origins and MIT
notices are in [the host provenance](../../src/NetWiv.BlazorHybrid/Aot/Hosting/README.md),
[the license](../../licenses/dotnet-runtime-MIT.txt) and
[third-party notices](../../THIRD-PARTY-NOTICES.md).
