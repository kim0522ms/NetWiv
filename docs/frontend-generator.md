# Generated frontend bindings

`NetWiv.Generators` produces JavaScript calls and TypeScript declarations from
C# command signatures, and a typed C# client for .NET frontends such as Blazor. See the [quickstart](integration-quickstart.md) for
package setup and the support boundaries below before exposing custom DTOs.

## Build workflow

Reference `NetWiv.Generators` as an analyzer and opt in at assembly level:

```csharp
[assembly: GenerateFrontendBindings]
[assembly: CommandJsonContext(typeof(AppJsonContext))]
```

With `NetWiv.Build` referenced, set the output directory relative to the C# project:

```xml
<NetWivFrontendBindingsDirectory>../src/generated</NetWivFrontendBindingsDirectory>
```

MSBuild exports after compiling the managed assembly, including before NativeAOT.
To bootstrap a frontend before its first Vite build, use
`dotnet build src-netwiv -p:NetWivBindingsOnly=true -p:PublishAot=false`.
The React template runs this automatically and watches C# changes during dev.
Framework maintainers using source targets should add `--disable-build-servers`
so a reused MSBuild node does not lock the task DLL against a subsequent rebuild.

The standalone CLI export is also available:

```sh
dotnet build path/to/App.csproj
netwiv bindings path/to/bin/Debug/net8.0/App.dll frontend/src/generated
```

During framework development the CLI can be run with
`dotnet src/NetWiv.Cli/bin/Release/net8.0/netwiv.dll bindings ...` after building
the CLI. The exporter reads PE metadata, not a loaded assembly; module
initializers, static constructors and app setup do not execute. It needs the
managed DLL before NativeAOT compilation, not the final native executable.

The output is `commands.js` plus `commands.d.ts`:

```ts
import { commands } from './generated/commands.js';
const greeting = await commands.greet({ name: 'World' });
```

Wrappers call `@netwiv/api/core` invoke and pass InvokeOptions through. The
desktop facade privately bundles the pinned Tauri implementation;
see [the frontend SDK](frontend-api.md) for package setup and excluded APIs.
Command names and argument keys are preserved exactly. No-argument
commands accept an omitted argument object. The command registry still controls
exposure: generation does not call InvokeHandler, grant permissions, or expose
unregistered commands. The export includes local generated declarations and public
commands from opted-in compiler references; it does not infer runtime registration
decisions. Local `[Command]` methods on
`IPlugin` types export `plugin:<Name>|<command>` names. Their `Name` must be a
non-virtual, non-overridden constant expression-bodied getter so export can
determine the wire name without executing code (`DTOXGEN011` otherwise).
Runtime plugin registration has no such constant-name requirement.
See [plugin commands](plugin-commands.md) for explicit runtime registration.

When upgrading an existing project, install the matching `@netwiv/api` package
before regenerating bindings. The first facade is supplied as a portable local
package in new starters; it does not require an unpublished npm registry release.
Previously generated bindings importing the Tauri package must be regenerated
to use `@netwiv/api/core`; direct upstream imports are no longer supported. Remove
the old frontend dependencies and runtime-detection guards as described in
[the migration guide](frontend-api.md#installation-and-migration). Command names,
permissions and backend registration remain unchanged.

### Typed C# client for .NET frontends

A .NET frontend such as Blazor WebAssembly can call the same commands through
generated C# methods instead of command-name strings. Set a second output
directory in the C# app project:

```xml
<NetWivCSharpBindingsDirectory>../frontend/obj/netwiv/$(Configuration)</NetWivCSharpBindingsDirectory>
```

Each build then writes `NetWivCommands.g.cs` (namespace `NetWiv.Generated`) under
the frontend's configuration-specific intermediate directory. Include that file
in the frontend's `Compile` items after generation; the starter's `NetWiv.targets`
handles this ordering and regeneration. Register it and call the commands with
their C# types:

```csharp
builder.Services.AddScoped<NetWiv.Generated.NetWivCommands>();

// In a component: @inject NetWiv.Generated.NetWivCommands Commands
Greeting greeting = await Commands.GreetAsync("World");
```

The client does not redeclare DTOs. It uses the app's own parameter and result
types and its `CommandJsonContext`, so both sides serialize with the same
options. That requires those types and the JSON context to live in a library
that both the app and the frontend reference, such as the Blazor starter's
`contracts/` project. A command whose types are declared in the app itself, or
in a NetWiv package, gets no method; the generated file lists it with the reason.
Only commands declared in the app are included, not commands from referenced
plugin assemblies. A command whose parameters and result are only `byte[]` or
`JsonElement` (or none) needs no shared context.

When `NetWivCSharpBindingsDirectory` is set and `NetWivFrontendBindingsDirectory`
is not, the app emits only the C# client: `commands.js` and `commands.d.ts` are
not generated, so wire types that the TypeScript mapping rejects (for example a
custom `[JsonConverter]`) do not fail the app build. `netwiv bindings` reports
that such an app has no JavaScript bindings.

Method names are the C# method name plus `Async`; two methods with the same name
fall back to the external command name, and a numeric suffix settles any
collision left. Each method takes an optional `CancellationToken`, which stops
waiting for the answer but does not cancel the command. Arguments are serialized
with the shared JSON context; top-level `byte[]` travels as an array of numbers
and `JsonElement` unchanged, as the command dispatcher expects. They are sent
through the frontend's `wwwroot/netwiv.js` module, whose `invokeJson`
function forwards them to `@netwiv/api/core`. A failed call surfaces as a
`JSException`. Renaming a command, a parameter or a DTO member in C# changes the
generated method or type, so the frontend build fails instead of the call failing
at run time. Exposure rules are unchanged: the client does not register commands
or grant permissions.

The bridge uses `@netwiv/api/core.invokeJson`: arguments and results stay JSON
text while crossing JavaScript, and are parsed only by C#. This preserves
`long`, `ulong`, `Int128`, `decimal` and nested `JsonElement` numeric values
without requiring `NumberHandling` overrides. The command and shared context
still determine the supported C# types and serialization rules. Ordinary
JavaScript `invoke` calls retain JavaScript's numeric limits.

`invokeJson(command, argsJson, options?)` requires a JSON object as its input
text and returns JSON text, including `"null"` for a null result. Binary responses
are rejected; top-level typed `byte[]` commands still use JSON numeric arrays.
The call uses the authenticated message bridge, including on hosts with protocol
IPC, with the same capability and document lifetime checks as `invoke`. Isolation
hooks receive the argument JSON as a string and must preserve it as text to avoid
rounding numbers. Older runtimes fail explicitly instead of silently falling back
to a lossy call. JavaScript callers that use `JSON.parse` on the result still have
JavaScript's numeric limits.

The Blazor starter imports `frontend/NetWiv.targets`, which refreshes the client
before the frontend compiles whenever the app's or the contracts' C# sources or
project files changed (including deleted files), using a bindings-only app build
of the same configuration. The project's root `Directory.Build.props` gives that
build its own `obj/bindings` and `bin/bindings` folders in the app and in
`contracts/`, so it does not share files with a normal, IDE or NativeAOT build
running at the same time; it also imports any `Directory.Build.props` further
up. The client is written to `frontend/obj/netwiv/$(Configuration)/NetWivCommands.g.cs`
and compiled only for that configuration. This isolates overlapping Debug and
Release builds and keeps generated code under the existing `obj/` Git ignore.
The client file is rewritten only when its content changes.

### Referenced command assemblies

Both the consumer and producer opt in with `[assembly: GenerateFrontendBindings]`
and reference `NetWiv.Generators` as an analyzer. The producer's managed build
contains a versioned command index; it does not need `NetWiv.Build`, an output
directory, or a separate frontend export. Project references and NuGet packages
use the same contract. Rebuild older producers with this generator before
consuming their bindings; missing or incompatible metadata produces `DTOXGEN013`.

The consumer aggregates commands declared in the assemblies already visible to
its C# compilation. It does not walk assembly dependencies, load assemblies, run
plugin getters or re-export another producer's aggregated index. The SDK may
supply transitive packages as compiler references; such opted-in assemblies also
participate. Commands in dependencies absent from the compilation need a reference
or a separate export. Non-opted-in producers keep their runtime behavior but do
not contribute frontend bindings.

Only public command methods whose complete containing-type chain is public
contribute to other assemblies. Internal commands remain in their own project's
frontend export. Imported declarations are checked against their actual public
symbols, `[Command]` names and plugin shape. Plugin routes retain the constant
name proven when the producer was compiled; consumers cannot reevaluate source
expressions from a DLL. The producer's generated metadata is a build contract,
not a runtime permission or plugin installation mechanism.

Each command retains its producer's `CommandJsonContext`, including directional
member rules, naming and numeric policies. A consumer context cannot overwrite
that contract. DTOs shared by distinct JSON contexts receive distinct, deterministic
interface names when necessary. Duplicate final command routes across local and
referenced commands fail with `DTOXGEN003`; they are never silently replaced.
Unsupported JSON metadata remains `DTOXGEN009`, and malformed/version-unknown
command metadata is `DTOXGEN013`.

The command index uses `AssemblyMetadata` keys `NetWiv.Frontend.Commands.v1`
(value `1`) and `NetWiv.Frontend.Command.v1:<method declaration ID>` (value
`<declaring metadata type name>\n<final wire route>`). It contains producer-local
public commands only. These keys are generated implementation metadata; do not
author or edit them. The exported JS/TS metadata and PE-only exporter are unchanged.

Generation includes unselected declarations. Application commands still require
`InvokeHandler(...)`/`RegisterGeneratedCommands()`, plugins still require explicit
installation and command selection, and capabilities/denials still control IPC.
Removing a reference and rebuilding removes its declarations from the export.

## Readable DTO names

Generated interfaces use the C# type name with an `Input` or `Output` suffix:

```csharp
public sealed record Greeting(string Message);
```

```ts
import { commands } from './generated/commands.js';
import type { GreetingOutput } from './generated/commands.js';

const greeting: GreetingOutput = await commands.greet({ name: 'World' });
```

Input and output remain separate because JSON serialization can give them
different required members, names and omission rules. Nested types retain the
containing type (`Envelope_EntryOutput`); constructed generics retain their
arguments (`PageOfUserOutput`). If different types share a name, their namespaces
disambiguate them (`Sales_UserOutput`, `Support_UserOutput`). The rare remaining
collision after flattening namespace/generic names receives a deterministic
type-identity hash. Names no longer depend on command traversal order; adding an
unrelated command does not renumber every DTO.

When the same DTO uses multiple JSON contexts, the hash also distinguishes their
assembly names and context type names. Assembly versions are excluded, so a
version-only upgrade of a producer or consumer preserves the exported names.

Migration: rebuild to regenerate `commands.d.ts`, then replace any direct imports
of old names such as `Dto17Output` with the corresponding named interface. Do not
edit generated files. Callers that infer results from `commands` need no naming
change. JavaScript command names, argument keys and JSON wire shapes are unchanged.

## Serialization and current boundaries

- Command injection parameters (AppHandle, State, InvokeContext, cancellation)
  do not appear in JS arguments. Channel inputs use the official Channel type.
- Primitive values, standard date/time/Guid/Uri strings, nullable values,
  arrays, supported generic collections, string-keyed dictionaries and ordinary
  DTO properties have frontend shapes. Void commands resolve to JSON null.
- Input/output DTO declarations are distinct. JSON names honor the default or
  all six built-in context naming policies and JsonPropertyName. JsonIgnore
  Always is omitted; conditional/default omissions are optional on output;
  required properties and non-nullable constructor parameters without defaults
  are required on input. Constructor selection follows JsonConstructor, class
  parameterless/single-public constructors and struct defaults, matching Core's
  generated JSON metadata validation. Nullable/default constructor parameters
  may be omitted. Recursive graphs, ordinary inherited
  properties and overrides are retained.
- String enum contexts and generic JsonStringEnumConverter annotations support
  exact JsonStringEnumMemberName values. Unnamed numeric values and flags retain
  their serializer-compatible fallback types.
- A top-level command byte array uses an array of numbers. Nested byte arrays
  use System.Text.Json's default base64 string. This distinction is tested.
- Input DTOs include public setters and constructor-bound properties. Computed
  getters and private setters are output-only unless a constructor consumes
  them; setter-only properties are input-only. JSON-ignored members are omitted
  before checking their converters or otherwise unsupported member attributes.
  Core also skips ignored/non-deserializable input members, while continuing to
  validate source-generated metadata for explicitly populated getter collections.
  Generator mapping for `JsonObjectCreationHandling` remains explicitly
  diagnosed with DTOXGEN009; it does not silently promise replacement semantics.
- The generator maps property-level `JsonIgnoreCondition`
  `WhenWriting` to input-only members and `WhenReading` to output-only setters.
  Constructor-bound properties still accept input with `WhenReading`, matching
  actual source-generated System.Text.Json 10 deserialization. Ignored directions do not
  require an otherwise unused converter/type mapping. It also
  accepts context-wide directional defaults. Actual System.Text.Json 10 source-generated
  metadata still reads members under `WhenReading`; `WhenWriting` output members
  without a property override are optional because the fast serializer omits
  them while metadata serialization includes them. Both paths have oracle tests.
- The generator maps public `[JsonInclude]` fields and context `IncludeFields`,
  including JSON names, required fields and constructor-bound readonly fields.
  Nullable enum members retain their generic string-enum converter and `null`.
  Nonpublic included fields/accessors remain explicit DTOXGEN009 diagnostics.
- JSON numeric values use TypeScript number, including C# long/ulong/decimal.
  Generated types do not expand JavaScript's numeric precision. Applications
  requiring exact values beyond that range must use an explicit string-based
  wire contract; the generator does not silently change the runtime encoding.
- The generator flattens `[JsonExtensionData]` dictionaries of
  `string`/`JsonElement` into `[key: string]: unknown` on the corresponding DTO
  interface. Known members retain their types and requiredness. Both Dictionary
  and IDictionary are supported; the CLR extension member is not a named wire
  property. Getter-only properties and directional ignores follow actual System.Text.Json 10
  serialization; included readonly fields can collect data. Additional output
  values require TypeScript narrowing. Object-valued dictionaries, required
  extension members, constructor-bound extension data and custom converters
  remain explicit diagnostics. These are pending mappings, not language exclusions.
- Explicit tagged JSON polymorphism produces unions as described below.
  Unmapped converters, polymorphic combinations, naming/serialization options and other
  unsupported types produce DTOXGEN009 rather than a false type or `any`.
  These are pending implementation areas, not language-impossibility exclusions
  or claims of completed generator coverage. Custom JSON contexts/resolvers and
  complex inherited/hidden property combinations need broader validation.
- MSBuild export, unchanged-file preservation, rename/removal invalidation and
  React template integration are implemented. Complete serializer parity, type
  mapping extension points remain in progress.
  The template's automated C# edit/restart evidence is recorded in
  [React development](react-development.md).

### Tagged JSON polymorphism

An abstract class or interface annotated with `[JsonPolymorphic]` and one or
more `[JsonDerivedType(typeof(Variant), discriminator)]` attributes maps to a
discriminated union. Roots and variants must use JSON object contracts;
variants must be sealed concrete classes assignable to the root.
Discriminators may be strings or 32-bit integers, including negative
integers. The default property name is `$type`; `TypeDiscriminatorPropertyName`
sets its exact JSON name independently of the context's naming policy.

```csharp
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TextItem), "text")]
[JsonDerivedType(typeof(CountItem), 7)]
public abstract class Item { public string? Note { get; set; } }
public sealed class TextItem : Item { public required string Text { get; set; } }
public sealed class CountItem : Item { public required int Count { get; set; } }
```

With a camel-case JSON context, the generated root keeps its stable DTO name:

```ts
export type ItemInput = (TextItemInput & { "kind": "text" })
  | (CountItemInput & { "kind": 7 });
export type ItemOutput = (TextItemOutput & { "kind": "text" })
  | (CountItemOutput & { "kind": 7 });
```

Variant interfaces retain inherited properties, required input members,
directional ignores and nullable members. Nullable roots also admit `null`;
recursive roots, collection elements and nested properties use the same union.
Each referenced command retains its producer's JSON context and discriminator
contract. The JSON context must supply source-generated metadata for the root
and its variants; `[JsonSerializable(typeof(Item))]` lets System.Text.Json
discover the declared derived types. No runtime type discovery is used.

Core validates the selected variant's source-generated metadata before command
deserialization, including required fields, non-nullable properties and numeric
shape/range rules. System.Text.Json still validates discriminator syntax,
unknown tags and metadata ordering. Construct input objects with the discriminator
as their first field under the supported context defaults:

```ts
await commands.echo({ value: { kind: "text", text: "hello" } });
```

TypeScript cannot enforce object property order. A type-correct object with its
discriminator after ordinary properties can still be rejected by the serializer.
Generated command results
serialize through the declared root metadata, so the discriminator and variant
properties appear on the actual wire.

Roots or variants implementing `IEnumerable` (including collection/dictionary
subclasses) or `IAsyncEnumerable<T>` remain `DTOXGEN009` in both directions.
System.Text.Json uses collection contracts for these types rather than their
ordinary CLR properties. For example, a tagged `List<int>` subclass uses
`{"$type":"items","$values":[1,2]}`. Async collection output requires
asynchronous JSON serialization, and the current System.Text.Json async
collection converter rejects polymorphic discriminator metadata. This
object-union mapping does not support those contracts; wrap a supported
synchronous collection in a property of an ordinary object variant instead.

Concrete roots, untagged variants, duplicate tags/types, nonsealed or abstract
variants, nested polymorphic variant declarations, extension data on variants,
and discriminator/member collisions remain `DTOXGEN009`. The collision check is
deliberately conservative: it also checks ignored/private instance members and
fields excluded by `IncludeFields`, and rejects ignored extension data declarations.
These combinations may work with System.Text.Json alone but are outside this
initial mapping; choose a discriminator name distinct from those members.
Reserved metadata
names `$id`, `$ref` and `$values` cannot be discriminators in this mapping.
`IgnoreUnrecognizedTypeDiscriminators = true` and fallback
`UnknownDerivedTypeHandling` modes also remain unsupported. Use the default
strict behavior (or explicitly `false` and `FailSerialization`) for this bounded
mapping. Arbitrary user converters remain unsupported; complete polymorphic
serializer parity is not claimed.

### Numeric serialization policies

`JsonSourceGenerationOptions.NumberHandling` and `JsonNumberHandling` on DTOs
and properties now participate in the generated input/output types. Explicit
property policies override the containing type; a containing type's policy does
not propagate into a nested object's properties. Numeric collection elements
follow their collection property policy. General, Web and Strict serializer
defaults are recognized; Web defaults accept quoted numeric input.

- `AllowReadingFromString`: numeric inputs use `number | string`.
- `WriteAsString`: numeric outputs use `string`.
- `AllowNamedFloatingPointLiterals`: floating-point types also admit the exact
  strings `"NaN"`, `"Infinity"` and `"-Infinity"` when a broader string type is
  not already required by another flag.
- Command-level `byte[]` keeps NetWiv's numeric-array codec. Nested `byte[]`
  remains the JSON serializer's base64 string.

All eight flag combinations are compared against source-generated .NET JSON
metadata, including null collection elements, invalid syntax and numeric ranges.
The IPC validator honors the same policies before deserialization. A TypeScript
`string` cannot prove numeric syntax or range; those are checked at invocation.
`AllowReadingFromString` alone does not promise that every accepted non-finite
value can also be serialized: the output policy still applies. Invalid explicit
number handling on a non-numeric property produces DTOXGEN009.
The underlying flag semantics follow the
[.NET 10 contract](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.serialization.jsonnumberhandling?view=net-10.0).

## Verification

`tests/NetWiv.Generators.Tests` covers command keys, injected parameters,
input/output JSON names, recursive nullable DTOs, byte-array shapes, unsupported
serialization features and metadata extraction without executing app code.
Referenced-command regressions cover implementation/reference assemblies, public
visibility, local/reference route collisions, per-producer JSON contexts, stable
DTO naming across producer/consumer assembly version changes and
malformed/version-unknown metadata.
Serializer comparisons use source-generated System.Text.Json metadata as the
reference. These verify the NetWiv generator contract rather than TAURI®'s Rust
business-logic implementation.

Export checks cover unchanged output timestamps, command rename/removal, empty
contracts and protection against overwriting handwritten files. The package
consumer harness in `tests/frontend-bindings/run.mjs` additionally checks
TypeScript accepted/rejected calls, generated React-to-C# IPC in managed and
NativeAOT WebViews, and consumption without Cargo. Its initialized-app scenario
packs a separate command/plugin project, consumes it through NuGet, checks strict
TypeScript contracts, and executes generated referenced-command wrappers in both
managed and NativeAOT WebViews. It also checks unregistered/denied plugin routes
and removes the package to verify export invalidation.

The consumer harness currently selects Windows/Linux x64 RIDs only. Do not run
it unchanged as macOS package-consumer proof. The release workflow separately
checks managed React and windowless NativeAOT package consumers on macOS;
see [macOS validation](macos.md#validation-and-limits).

Run from the repository root after creating a matching SDK/CLI package feed.
Replace `PACKAGE_VERSION` with the feed's version. `--new` selects CLI project
creation and does not require a separate `NetWiv.Templates` package:

```sh
npm ci --prefix tests/frontend-bindings
node tests/frontend-bindings/run.mjs /path/to/feed PACKAGE_VERSION PACKAGE_VERSION --new
```

For direct `dotnet new` validation, omit `--new` and supply an SDK version and
a template version whose packages are both available in that feed. The generated
consumer, package caches and build outputs are local test artifacts.

Linux can set `NETWIV_DOTNET` to an absolute dotnet executable,
`NETWIV_GUI_LAUNCHER` to `scripts/run-linux-smoke-desktop.sh`,
`NETWIV_GUI_BACKEND` to `x11` or `wayland`, and `NETWIV_TEST_ARTIFACTS` to an output
directory. A Wayland WSLg run needs the actual `WAYLAND_DISPLAY` socket path.
Keep Node/npm versions compatible with the template's Vite requirement.
[React development](react-development.md#automated-verification) covers the
separate edit/restart test. Automated native execution does not establish
manual GUI acceptance or full Tauri compatibility.

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.
