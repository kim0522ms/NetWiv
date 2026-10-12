using System.Text.Json;
using NetWiv;

namespace NetWiv.Plugin.CommandFixture;

// The consumer deliberately has no direct reference to this type. Its factory
// and generated handlers must survive NativeAOT through the exported metadata.
[Plugin]
public sealed class ExternalCommandPlugin : IPlugin
{
    public string Name => "generated-external";

    public void Initialize(PluginContext context)
    {
        context.RegisterGeneratedCommands<ExternalCommandPlugin>("get_status");
        context.Permission(new PermissionDefinition("allow-status", "get_status", "hidden"));
    }

    [Command]
    public static JsonElement GetStatus(JsonElement value) => value.Clone();

    [Command]
    public static JsonElement Hidden() => JsonValue.From("This command must never be exposed.");
}
