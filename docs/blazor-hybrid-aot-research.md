# Blazor Hybrid NativeAOT future work

Status: deferred research. The current delivery includes managed Hybrid as the
default and the existing static host as an explicitly selected experiment.
Broader AOT support, upstream library forks and submodule integration are future
work, outside this delivery and not release blockers. No fork has been added.
The source audit below records possible boundaries, not an approved
implementation schedule or a claim of full AOT support.
See [usage and current limits](blazor-hybrid.md).

## Multi-target adapter assessment

Extending the existing **experimental** contract to .NET 8/9/10 is a small
adapter/build change and has been implemented without a fork. The reviewed
package sources are ASP.NET Core `8.0.32` (`7413d24330774263a78a4f3837ca53493518455a`),
`9.0.21` (`b4d87e3b9131a019bc3b51df99ff69ac79bef459`) and dotnet/dotnet
`10.0.13` (`4099f83977f4d29604a9aeb971fb0a7bc1d5f699`).

| Difference | Implemented adjustment | Cost boundary |
| --- | --- | --- |
| Target/package identity | One NuGet package with three TFM assets, exact dependency pins and runtime guards | Build/package maintenance; no per-version public host API |
| .NET 9 renderer metadata | Interactive `WebView` `RendererInfo` | One conditional override |
| .NET 10 framework assets | Embed the unchanged official script and default module manifest at build time | Small file-provider adapter; RCL initializer discovery remains outside the existing contract |
| .NET 10 JS invocation kinds | Send the call-kind field and implement constructor/get/set operations for global and object targets | Existing typed serializer, pending calls and lifetime logic are reused |
| Render batches and incoming IPC | Reviewed serializers/receiver are unchanged across these pins | No renderer/diff engine rewrite |

The larger work is regression and package-consumer coverage: source builds under
older SDKs must remain possible, while release packing must include all three
targets. Native execution still checks that reflection JSON and dynamic code
are disabled. The known upstream `IL2072`/`IL3050` diagnostics remain visible;
the focused experimental test project and opted-in sample permit these codes
without suppressing them. The package does not weaken consumer warning policy.
This is not a warning-free or full Blazor NativeAOT implementation.
Removing manual registration, supporting ordinary routing/streams and arbitrary
Razor libraries is still the separate future work described below. Multi-targeting
does not reduce that work to a package-version change.

## Objective and acceptance contract

The objective is to preserve ordinary Blazor application code while publishing
a statically known application as NativeAOT. A passing counter sample is
insufficient. The same application must exercise components, forms, routing,
standard JS interop and document lifetime in both managed and native builds.

The supported graph must be known at build time: application assemblies,
referenced Razor class libraries (RCLs), concrete generic component types and
JSON contracts. Runtime loading of arbitrary new IL is not part of this
contract. This is a NativeAOT constraint, not something a blanket metadata root
or a compiler fork automatically removes. See Microsoft's
[NativeAOT limitations](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/#limitations-of-native-aot-deployment).

Do not call this complete until the supported graph passes:

- Ordinary Razor child components, property injection, parameters and cascades
  without manual lists in every application.
- Standard `Router` behavior, including route parameters, not-found handling
  and navigation callbacks; cancellation during navigation and document close.
- Explicitly specified form/binding cases, with unsupported expressions or
  types diagnosed rather than silently accepted.
- Standard `IJSRuntime`, `DotNetObjectReference<T>` and `[JSInvokable]` contracts,
  including asynchronous results and .NET/JS reference disposal.
- Custom events and both streaming directions with lifetime and cancellation
  checks.
- Source-generated JSON with reflection serialization disabled, and no
  unexplained AOT/trimming warnings in the supported application graph.
- A separate packaged consumer running the real native WebView, including
  reload, close, errors and asynchronous scoped cleanup.

No unrestricted compatibility promise is made for precompiled third-party
libraries. Some need their own generated manifests or changes.

## Where the blockers are

The baseline source is ASP.NET Core 8.0.32 at
[`7413d243`](https://github.com/dotnet/aspnetcore/tree/7413d24330774263a78a4f3837ca53493518455a).
The following are source-based implementation proposals, not verified patches.

| Area | Current mechanism and limitation | Smallest plausible change |
| --- | --- | --- |
| WebView envelopes | `IpcCommon` serializes/deserializes transport values through reflection JSON. | Fixed envelope readers/writers or generated metadata inside the WebView transport. |
| JS result completion | `JSRuntime.EndInvokeJS` uses `TaskGenericsUtil` to create generic completion helpers at runtime. | Store a typed pending-call handler when `InvokeAsync<T>` is compiled. |
| JS-invokable dispatch | `DotNetDispatcher` searches assemblies/types and invokes methods reflectively; generic `ValueTask<T>` conversion uses runtime generic construction. | Generated dispatch manifests with typed delegates and statically closed await/result handlers. |
| Object references | The converter factory creates generic converters dynamically. Standard reference disposal calls internal `JSRuntime` ownership methods. | Register closed converters inside a patched JSInterop implementation while preserving its original Track/Get/Release ownership. |
| Component metadata | `ComponentFactory`, `ComponentProperties` and `CascadingParameterState` obtain types through `GetType()`. Public component registration does not connect this to the analyzer's metadata requirements. | Core metadata registration hooks populated with `typeof(T)` and preserved members; unknown types fail explicitly on the AOT path. |
| Routing | `RouteTableFactory` discovers components from assemblies. `Router`'s internal state also controls location-change handling. | Generated route information plus a narrowly adapted routing-provider hook, preserving navigation and not-found semantics. |
| Navigation locks | The pinned `NavigationManager` does not expose document-retirement cancellation for its pending handler state. | A Core navigation lifetime hook and WebView integration, with pending-handler close/reload tests. |
| Forms and binding | `BindConverter` dynamically closes some enum/array converters. `FieldIdentifier` and expression formatting depend on expression shape and preserved metadata. | Generated converters/metadata or narrowly scoped Components/Forms hooks; test each supported shape. |
| Static assets | Output staging and Debug manifests are host/build concerns. | Adapter/MSBuild integration; no renderer or runtime fork is needed. |

Relevant source: [IPC](https://github.com/dotnet/aspnetcore/blob/7413d24330774263a78a4f3837ca53493518455a/src/Components/WebView/WebView/src/IpcCommon.cs),
[JSRuntime](https://github.com/dotnet/aspnetcore/blob/7413d24330774263a78a4f3837ca53493518455a/src/JSInterop/Microsoft.JSInterop/src/JSRuntime.cs),
[dispatch](https://github.com/dotnet/aspnetcore/blob/7413d24330774263a78a4f3837ca53493518455a/src/JSInterop/Microsoft.JSInterop/src/Infrastructure/DotNetDispatcher.cs),
[reference ownership](https://github.com/dotnet/aspnetcore/blob/7413d24330774263a78a4f3837ca53493518455a/src/JSInterop/Microsoft.JSInterop/src/DotNetObjectReferenceOfT.cs),
[component parameters](https://github.com/dotnet/aspnetcore/blob/7413d24330774263a78a4f3837ca53493518455a/src/Components/Components/src/Reflection/ComponentProperties.cs),
[binding](https://github.com/dotnet/aspnetcore/blob/7413d24330774263a78a4f3837ca53493518455a/src/Components/Components/src/BindConverter.cs)
and [field identifiers](https://github.com/dotnet/aspnetcore/blob/7413d24330774263a78a4f3837ca53493518455a/src/Components/Forms/src/FieldIdentifier.cs).

An expression's use of `Compile()` is not by itself proof that NativeAOT cannot
execute it: .NET provides an interpreter fallback. Expression shape and
metadata availability still require testing. Do not replace this subsystem
solely because an AOT warning mentions expressions.

Static assets also need a concrete integration change: the experimental host
currently reserves `_framework/*` for embedded content, while WebView loads the
application/RCL initializer manifest from `_framework/blazor.modules.json`.
Use SDK-resolved static assets and a controlled manifest mapping while retaining
the pinned transport script. Validate `_content`, scoped CSS, JS initializers
and relocated publish output; this is build/host work rather than Renderer work.
See the [WebView initializer loader](https://github.com/dotnet/aspnetcore/blob/7413d24330774263a78a4f3837ca53493518455a/src/Components/Web.JS/src/JSInitializers/JSInitializers.WebView.ts).

## Why an external adapter alone is insufficient

Copying an internal helper into NetWiv does not change callers inside the
original Microsoft assembly. Likewise, types with the same namespace but
different assembly identities are not interchangeable.

The standard object-reference API illustrates this boundary. Its disposal
connects to JSInterop's internal reference table. A separate implementation of
`IJSRuntime` cannot attach to that table through public APIs, which is why the
current experiment has its own lease. A focused JSInterop patch can potentially
preserve the standard reference rather than adding a permanent alternative API.

There are also callbacks inaccessible to an application generator. Microsoft's
[`WebRenderer`](https://github.com/dotnet/aspnetcore/blob/7413d24330774263a78a4f3837ca53493518455a/src/Components/Web/src/WebRenderer.cs)
uses an internal nested interop target. Fully static dispatch needs a shim
generated or compiled inside that assembly. An RCL with inaccessible callback
types has the same issue. Application-only scanning cannot solve every library.

A component generator can discover statically referenced types, emit direct
factories and register metadata. It cannot by itself remove unannotated
`GetType()` paths inside the pinned Components assembly. A possible Core patch
prepopulates the existing injector, parameter-writer and cascade caches from
annotated concrete types, and disables reflection fallback on the AOT path.
The existing renderer/diff algorithms need not be replaced for that proposal.

Do not assume one Roslyn generator can emit a JSON context for another generator
to consume in the same run. Initially consume application-authored contexts.
Automatic context generation needs a separate pre-compilation stage or another
explicitly designed metadata path. See the
[Roslyn generator cookbook](https://github.com/dotnet/roslyn/blob/main/docs/features/source-generators.cookbook.md).

## What to fork, if necessary

| Option | Scope | Assessment |
| --- | --- | --- |
| Adapter and generated registrations only | NetWiv transport, analyzers/generators and finite application metadata | Useful first experiments, but does not establish full standard API/RCL compatibility or eliminate internal upstream warnings. |
| Scoped ASP.NET Core library fork | Selected changes in Components, Components.Forms, Components.Web, JSInterop and WebView, as required by the acceptance contract | The candidate for broader support. It is a maintained source/build graph, not a handful of copied helper files. Keep Renderer/diff algorithms unchanged. |
| NativeAOT runtime/compiler fork | `dotnet/runtime`, CoreLib, NativeAOT runtime, ILCompiler and runtime packs | No current blocker demonstrates a need for this. Do not start it without an isolated compiler/runtime failure that cannot be addressed in the managed library layer. |

The first fork experiment must prove assembly/package integration before a
large implementation: one patched JSInterop, the official Components graph,
and a separately compiled RCL must build and execute together. Check assembly
identity, signing, type identity, dependency resolution and version conflicts.
Renaming a package does not prove this works. A complete coherent library graph
may have to be rebuilt; it must not silently mix incompatible replacements.

Retain upstream licenses and provenance, pin the source/build inputs, keep
patches small, and define an update/security-patch process. Suppressing warnings,
rooting every framework assembly or marking unexecuted paths as supported does
not satisfy the objective.

## Current upstream direction

The 2026-10-20 audit also inspected upstream main at
[`3595620c`](https://github.com/dotnet/aspnetcore/tree/3595620ce66db77d795b5435ba44687dc94cf3ef).
The open [Blazor NativeAOT tracking issue](https://github.com/dotnet/aspnetcore/issues/51598)
is associated with .NET 12 planning at that audit point. Its scope concerns
Blazor inside ASP.NET Core; it is not a declaration of Hybrid support.

Main adds useful seams such as
[`IComponentPropertyActivator`](https://github.com/dotnet/aspnetcore/blob/3595620ce66db77d795b5435ba44687dc94cf3ef/src/Components/Components/src/IComponentPropertyActivator.cs)
and typed pending JS calls. These are backport/design candidates, not evidence
that a version upgrade resolves WebView serialization, dispatch discovery,
binding and library metadata together.

## Possible future research chunks and stop conditions

Resume these investigations only when the project explicitly selects this work.

1. **Assembly integration and standard reference spike.** Build the smallest
   patched JSInterop graph with a separate RCL. Exercise standard object
   references, both disposal directions and a real native callback. Stop and
   reassess if assembly replacement cannot preserve the intended public types.
2. **Static dispatch and JSON contract.** Generate per-assembly invokable
   manifests, typed pending calls and closed converters. Exercise Task/ValueTask,
   void, exceptions, nested DTOs, explicit polymorphism and inaccessible library
   targets. Reject missing contracts before executing JS.
3. **Component metadata and routing hooks.** Add the minimum Core metadata
   seams and generated component/route tables. Prove injection, parameters,
   cascades, route changes and cancellation while retaining the original
   Renderer/diff implementation.
4. **Forms, events, streams and asset integration.** After the earlier seams
   are stable these can be investigated independently. Define concrete supported
   cases, error handling, resource ownership and close/reload behavior.
5. **Compatibility and performance acceptance.** Run the same application and
   separate package consumer under managed and NativeAOT hosts. Compare startup,
   steady-state memory, event latency and sustained rendering with the same
   inputs; no performance benefit or regression has yet been established.

Chunks 1–3 determine whether the scoped library fork is viable. Chunk 4 expands
its contract, and chunk 5 is required before promoting it out of experimental
status. If core Renderer/diff reimplementation becomes necessary, stop and
report the changed scope before proceeding. A runtime/compiler fork also needs
a separate decision based on a demonstrated blocker.
