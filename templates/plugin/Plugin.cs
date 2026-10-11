using System.Text.Json.Nodes;
using NetWiv;

namespace NetWiv.Plugin.__PLUGIN_PASCAL__;

[Plugin]
public sealed class __PLUGIN_PASCAL__Plugin : IPlugin
{
    public string Name => "__PLUGIN_NAME__";

    public void Initialize(PluginContext context)
    {
        context.Permission(new PermissionDefinition("default", "ping"));
        context.RegisterCommand("ping", static (_, _, _) => ValueTask.FromResult(JsonValue.From("pong")));
    }
}
