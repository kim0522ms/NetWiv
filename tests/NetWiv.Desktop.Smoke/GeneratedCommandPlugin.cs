using System.Text.Json;
using NetWiv;

internal sealed class GeneratedCommandPlugin : IPlugin
{
    public string Name => "generated-local";

    public void Initialize(PluginContext context)
    {
        context.RegisterGeneratedCommands<GeneratedCommandPlugin>("get_status", "blocked");
        context.Permission(new PermissionDefinition("allow-status", "get_status"));
        context.Permission(new PermissionDefinition("allow-blocked", "blocked"));
        context.Permission(new PermissionDefinition("deny-blocked", [], ["blocked"]));
    }

    [Command]
    public static JsonElement GetStatus() => JsonValue.From("local");

    [Command]
    public static JsonElement Blocked() => JsonValue.From("Permission checks must reject this command.");
}
