# Generated frontend bindings

`NetWiv.Generators` produces JavaScript calls and TypeScript declarations from
C# command signatures. See the [quickstart](integration-quickstart.md) for
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
netwiv bindings path/to/bin/Debug/net10.0/App.dll frontend/src/generated
```

During framework development the CLI can be run with
`dotnet src/NetWiv.Cli/bin/Release/net10.0/netwiv.dll bindings ...` after building
the CLI. The exporter reads PE metadata, not a loaded assembly; module
initializers, static constructors and app setup do not execute. It needs the
managed DLL before NativeAOT compilation, not the final native executable.

The output is `commands.js` plus `commands.d.ts`:

```ts
import { commands } from './generated/commands.js';
const greeting = await commands.greet({ name: 'World' });
```

Wrappers call unchanged `@tauri-apps/api/core` invoke and pass InvokeOptions
through. Command names and argument keys are preserved exactly. No-argument
commands accept an omitted argument object. The command registry still controls
exposure: generation does not call InvokeHandler, grant permissions, or expose
unregistered commands. The current export includes local generated declarations;
it does not infer runtime registration decisions. Plugin/reference-assembly
binding aggregation is not yet implemented.

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
  actual source-generated .NET 10 deserialization. Ignored directions do not
  require an otherwise unused converter/type mapping. It also
  accepts context-wide directional defaults. Actual .NET 10 source-generated
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
  property. Getter-only properties and directional ignores follow actual .NET 10
  serialization; included readonly fields can collect data. Additional output
  values require TypeScript narrowing. Object-valued dictionaries, required
  extension members, constructor-bound extension data and custom converters
  remain explicit diagnostics. These are pending mappings, not language exclusions.
- Unmapped converters, polymorphism, naming/serialization options and other
  unsupported types produce DTOXGEN009 rather than a false type or `any`.
  These are pending implementation areas, not language-impossibility exclusions
  or claims of completed generator coverage. Custom JSON contexts/resolvers and
  complex inherited/hidden property combinations need broader validation.
- MSBuild export, unchanged-file preservation, rename/removal invalidation and
  React template integration are implemented. Complete serializer parity, type
  mapping extension points and referenced-command aggregation remain in progress.
  The template's automated C# edit/restart evidence is recorded in
  [React development](react-development.md).

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
Serializer comparisons use source-generated System.Text.Json metadata as the
reference. These verify the NetWiv generator contract rather than TAURI®'s Rust
business-logic implementation.

Export checks cover unchanged output timestamps, command rename/removal, empty
contracts and protection against overwriting handwritten files. The package
consumer harness in `tests/frontend-bindings/run.mjs` additionally checks
TypeScript accepted/rejected calls, generated React-to-C# IPC in a real WebView,
NativeAOT publication and consumption without Cargo.

The consumer harness currently selects Windows/Linux x64 RIDs only. Do not run
it unchanged as macOS package-consumer proof; the macOS NativeAOT smoke is a
source-linked integration check. See [macOS validation](macos.md#validation-and-limits).

Run from the repository root after creating a matching SDK/CLI package feed.
Replace `PACKAGE_VERSION` with the feed's version. `--new` selects CLI project
creation and does not require a separate `NetWiv.Templates` package:

```sh
npm ci --prefix tests/frontend-bindings
node tests/frontend-bindings/run.mjs /path/to/feed PACKAGE_VERSION PACKAGE_VERSION --new
```

For direct `dotnet new` validation, omit `--new` and supply an SDK version and
a template version whose packages are both available in that feed. The harness
writes package hashes and per-check outcomes to its output directory.

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
