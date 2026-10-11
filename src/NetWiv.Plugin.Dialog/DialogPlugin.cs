using System.Text.Json;
using NetWiv.Runtime;

namespace NetWiv.Plugin.Dialog;

/// <summary>Desktop dialogs compatible with the official @tauri-apps/plugin-dialog guest.</summary>
public sealed class DialogPlugin : IPlugin
{
    public string Name => "dialog";
    /// <summary>Raised after a selected path has been granted to the session scopes.</summary>
    public event Action<string, bool, bool>? PathSelected;

    public void Initialize(PluginContext context)
    {
        foreach (var command in new[] { "open", "save", "message" })
        {
            var name = command;
            context.RegisterResponseCommand(name, (invoke, args, token) => Execute(name, invoke, args, token));
            context.Permission(new("allow-" + name, [name], []));
            context.Permission(new("deny-" + name, [], [name]));
        }
        context.Permission(new("allow-ask", ["message"], []));
        context.Permission(new("deny-ask", [], ["message"]));
        context.Permission(new("allow-confirm", ["message"], []));
        context.Permission(new("deny-confirm", [], ["message"]));
        context.PermissionSet("default", "allow-open", "allow-save", "allow-message");
    }

    private static async ValueTask<RuntimeInvokeResponse> Execute(string command, InvokeContext context, JsonElement args, CancellationToken token)
    {
        var result = await context.InvokePlatformAsync("plugin:dialog|" + command, args, token);
        if (command is "open" or "save" && !result.IsBinary && result.Json.ValueKind != JsonValueKind.Null)
        {
            var options = args.TryGetProperty("options", out var value) ? value : default;
            var directory = command == "open" && Flag(options, "directory");
            var recursive = Flag(options, "recursive");
            if (result.Json.ValueKind == JsonValueKind.String)
                Grant(context.App, result.Json.GetString()!, directory, recursive);
            else if (result.Json.ValueKind == JsonValueKind.Array)
            {
                foreach (var path in result.Json.EnumerateArray())
                    Grant(context.App, path.GetString()!, directory, recursive);
            }
        }
        return result;
    }

    private static bool Flag(JsonElement options, string name) => options.ValueKind == JsonValueKind.Object &&
        options.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static void Grant(AppHandle app, string path, bool directory, bool recursive)
    {
        app.TryPlugin<NetWiv.Plugin.Fs.FsPlugin>()?.GrantSelectedPath(path, directory, recursive);
        var assets = app.TryAssetProtocolScope();
        if (assets is not null)
        {
            if (directory) assets.AllowDirectory(path, recursive: true);
            else assets.AllowFile(path);
        }
        app.TryPlugin<DialogPlugin>()?.PathSelected?.Invoke(path, directory, recursive);
    }
}
