# Plugin configuration

For `[Command]` handlers and explicit plugin registration, see
[generated plugin commands](plugin-commands.md).

Place plugin values under the exact plugin name in `netwiv.conf.json`:

```json
{
  "plugins": {
    "example": { "message": "Hello", "count": 3 }
  }
}
```

`UseEmbeddedConfig()` loads the values before `IPlugin.Initialize`. A plugin
receives only its own value through `PluginContext.Config`. Missing values are
JSON null, matching TAURI®'s plugin store. Object, array, scalar and explicit null
values are preserved. Configuration overlays merge before plugin initialization.

For typed settings, supply generated System.Text.Json metadata:

```csharp
using System.Text.Json.Serialization;
using NetWiv;

public sealed record Options(string Message, int Count);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Options))]
internal partial class PluginJson : JsonSerializerContext;

public sealed class ExamplePlugin : IPlugin
{
    public string Name => "example";

    public void Initialize(PluginContext context)
    {
        var options = context.ReadConfig(PluginJson.Default.Options)
            ?? throw new InvalidOperationException("Example plugin configuration is required.");
        context.Permission(new PermissionDefinition("allow-message", "message"));
        context.RegisterCommand("message", (_, _, _) =>
            ValueTask.FromResult(JsonValue.From(options.Message)));
    }
}
```

Register with `builder.Plugin(new ExamplePlugin())`, and grant
`example:allow-message` to the intended window capability before frontend code
calls `invoke('plugin:example|message')`. Plugin settings do not grant IPC access.

`ReadConfig` uses the supplied metadata without reflection-based serializer
discovery. JSON failures identify `plugins.<name>` and preserve the underlying
exception. Construction failure disposes plugins already initialized, including
the plugin whose initialization failed. The cloned JSON remains valid after the
configuration reader closes its document.

This maps Tauri's typed `PluginApi::config()` to C# generated JSON metadata.
Parser-specific diagnostic details follow System.Text.Json; plugin value
selection, missing/null handling and initialization failure are preserved.
The desktop conformance fixture `plugin.configuration` compares typed setup
and a capability-authorized plugin command against the pinned Tauri runtime.

Permission sets can include this plugin's permissions and qualified permissions
from another plugin:

```csharp
context.PermissionSet("default", "allow-message");
context.PermissionSet("with-path", "default", "core:path:allow-resolve-directory");
```

Grant `example:with-path` in a capability to use the set. Each included command
retains its own allow/deny scopes. A plugin with no declared default gets an empty
default set, which grants nothing. The `default` set and named permission sets
correspond to Tauri plugin permission manifests; C# plugins declare them during
static initialization. Referenced plugin sets resolve after every plugin has
initialized, so registration order does not determine access. Unknown references
and cycles fail application construction. Deny permissions retain precedence.

`netwiv plugin new` uses the CLI's SDK version for NuGet references;
`--netwiv-source` explicitly selects source references for framework development.

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.
