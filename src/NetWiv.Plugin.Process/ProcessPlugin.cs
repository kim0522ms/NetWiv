using System.Text.Json;

namespace NetWiv.Plugin.Process;

/// <summary>Exit and restart commands used by the official process guest.</summary>
public sealed class ProcessPlugin : IPlugin
{
    public string Name => "process";

    public void Initialize(PluginContext context)
    {
        context.RegisterCommand("exit", (invoke, args, _) =>
        {
            var code = args.TryGetProperty("code", out var value) ? value.GetInt32() : 0;
            invoke.App.Exit(code);
            return ValueTask.FromResult(JsonValue.Null);
        });
        context.RegisterCommand("restart", (invoke, _, _) =>
        {
            invoke.App.Restart();
            return ValueTask.FromResult(JsonValue.Null);
        });
        context.Permission(new("allow-exit", ["exit"], []));
        context.Permission(new("deny-exit", [], ["exit"]));
        context.Permission(new("allow-restart", ["restart"], []));
        context.Permission(new("deny-restart", [], ["restart"]));
        context.PermissionSet("default", "allow-exit", "allow-restart");
    }

}
