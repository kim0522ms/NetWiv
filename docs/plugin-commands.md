# Generated plugin commands

An `IPlugin` implementation owns its commands, name and permissions. It can
represent an application feature such as inventory or accounts; it does not
need to be a separately distributed add-on. Multiple plugins may live in the
same application assembly.

TAURI® [registers a handler per plugin](https://v2.tauri.app/develop/plugins/#adding-commands)
and addresses it as `plugin:<name>|<command>`. NetWiv uses the same wire naming
and existing plugin ACL path. Its C# counterpart is an `IPlugin` type with
generated static command handlers selected in `Initialize`:

```csharp
using System.Text.Json;
using NetWiv;

public sealed class InventoryPlugin : IPlugin
{
    public string Name => "inventory";

    public void Initialize(PluginContext context)
    {
        context.RegisterGeneratedCommands<InventoryPlugin>("get_status");
        context.Permission(new PermissionDefinition("allow-status", "get_status"));
    }

    [Command]
    public static JsonElement GetStatus() => JsonValue.From("ready");
}

// In the application's existing builder pipeline:
// .Plugin(new InventoryPlugin())
// .Capability(new("main", ["inventory:allow-status"], Windows: ["main"]))
```

Reference `NetWiv.Generators` as an analyzer in the project declaring commands.
For typed arguments/results, declare `[assembly: CommandJsonContext(...)]` and
the corresponding `JsonSerializable` types in that project, just as for
[application commands](../src/NetWiv.Generators/README.md). `JsonElement` in
this example needs no application serializer context. Async returns, argument
validation, state/service injection and cancellation use the same generated
handlers as application commands. Command methods remain static.

```ts
import { invoke } from '@tauri-apps/api/core';
const status = await invoke<string>('plugin:inventory|get_status');
```

An `AccountsPlugin` can independently declare `GetStatus`, register
`"get_status"` and use `Name => "accounts"`. Its frontend route is
`plugin:accounts|get_status`; neither command occupies the application route
`get_status`. A duplicate local command name in one plugin type is a generator
error. Duplicate final registrations still fail at application build time.

## Registration and ownership

- Only `[Command]` methods declared directly on a type implementing `IPlugin`
  belong to that plugin. Ordinary classes keep the application-command path.
  Namespaces, folders and assemblies do not assign plugin ownership. Use partial
  declarations to split one plugin type across files. Inherited command methods
  are not collected for derived plugin types.
- `RegisterGeneratedCommands<TPlugin>(...)` takes local external names, after
  snake-case conversion or explicit `[Command("name")]` overrides. Do not pass
  a `plugin:` prefix or a C# method name unless it is also the external name.
  The type must be the plugin currently being initialized. Missing names fail
  before any selected handler is registered.
- Generating a binding does not expose it. Each plugin explicitly selects its
  commands; automatic selection is outside this change. The application's
  `Builder.InvokeHandler(...)` and `Builder.RegisterGeneratedCommands()` only
  select application commands. Permissions and capabilities remain explicit;
  generation never grants access, and deny permissions still win.
- Existing `PluginContext.RegisterCommand` and `RegisterResponseCommand`
  remain available for handwritten handlers. Generated handlers can coexist
  with them under different local names.
- Use `Builder.Plugin(new InventoryPlugin())` for explicit plugin installation.
  The existing `[Plugin]` attribute remains an optional factory-generation path;
  it is not required to generate plugin commands. Separate plugin assemblies
  use the same registration API and generated module initializers, including
  under NativeAOT. Runtime reflection-based method discovery is not used.

Previously, `[Command]` on an `IPlugin` class entered the application registry.
After rebuilding with this generator it belongs to that plugin. Move the method
to an ordinary command class to retain an application route, or select it in
`Initialize` and update callers/capabilities to the plugin route. Other existing
application commands and handwritten plugin registrations are unchanged.

## Optional frontend bindings

With `[assembly: GenerateFrontendBindings]`, local plugin declarations export
qualified names such as `commands['plugin:inventory|get_status']()`.
The generator must prove the plugin name without running application code:
use a non-virtual, non-overridden constant expression-bodied `Name` getter,
as above. Unsupported getters produce `DTOXGEN011` during frontend export.
Runtime-only plugin registration still supports names chosen at runtime.

The exporter includes declared commands, including ones not selected for
registration. It does not infer execution of `Initialize`, and it does not
aggregate frontend bindings from referenced assemblies. Export bindings in the
plugin's own project or use `invoke` directly. See the
[frontend generator contract](frontend-generator.md).

This implements plugin-scoped command generation and explicit registration;
it is not a claim of complete upstream plugin API or mobile support.

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.
