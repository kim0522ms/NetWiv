# Experimental NativeAOT Blazor product tests

This executable uses the product `NetWiv.BlazorHybrid` package sources. It contains no
copy of the rendering host or interop runtime. The project explicitly opts into
`NetWivBlazorExperimentalAot`; passing it does not establish full Blazor AOT support. Microsoft's unchanged `Renderer`
still owns component state, event dispatch and diffing.

Both modes assert `JsonSerializer.IsReflectionEnabledByDefault == false`.
NativeAOT execution also asserts `RuntimeFeature.IsDynamicCodeSupported == false`;
`--managed` explicitly selects the managed development check.

## Windows execution

Publishing alone does not pass the execution gate:

```powershell
dotnet publish tests/NetWiv.BlazorHybrid.Aot.Tests -c Release -r win-x64 -o artifacts/blazor-aot-tests
& ./artifacts/blazor-aot-tests/NetWiv.BlazorHybrid.Aot.Tests.exe
& ./artifacts/blazor-aot-tests/NetWiv.BlazorHybrid.Aot.Tests.exe --native-webview
```

The first invocation injects protocol messages and inspects actual serialized
render batches from the product session. It also wraps a controlled native
boundary in the production `BlazorDesktopRuntime` to test origin, webview label,
document identity, reload retirement and closed-document rejection.

The second uses the production `BlazorDesktopRuntime(new DesktopRuntime(), ...)`,
actual WebView2, packaged Microsoft `blazor.webview.js` and real DOM events. It
reloads the browser document once, repeats its assertions and closes the window.
It requires WebView2 and the usual native backend artifacts described in
[development](../../docs/development.md). Browser assertions are automated;
they are separate from manual visual and IDE debugger acceptance.

For managed development checks:

```powershell
dotnet run --project tests/NetWiv.BlazorHybrid.Aot.Tests -c Release -p:PublishAot=false -- --managed
dotnet run --project tests/NetWiv.BlazorHybrid.Aot.Tests -c Release -p:PublishAot=false -- --managed --native-webview
```

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

Protocol checks do not prove DOM behavior. Execute both modes on the native
artifact before claiming NativeAOT browser acceptance. Other operating systems,
arbitrary Razor libraries, streams and navigation locks are outside these tests.

## Trimming and provenance

There are no framework-wide trimming roots or fallback reflection JSON resolver.
Only explicitly registered concrete components preserve their metadata. The
pinned Microsoft Components assembly can emit `IL2072` diagnostics because its
property injection, parameter and cascading paths obtain types from
`object.GetType()`. Keep these diagnostics visible and verify the registered
component paths in the native executable; this does not establish arbitrary
runtime-discovered component support.
The full form sample has additional upstream AOT diagnostics and a narrower
tested binding contract; see [NativeAOT limits](../../docs/blazor-hybrid.md#experimental-nativeaot-limits).

Microsoft packages are pinned to `8.0.32`. Adapted product source origins and MIT
notices are in [the host provenance](../../src/NetWiv.BlazorHybrid/Aot/Hosting/README.md),
[the license](../../licenses/dotnet-runtime-MIT.txt) and
[third-party notices](../../THIRD-PARTY-NOTICES.md).
