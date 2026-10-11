using System.Text.Json;

namespace NetWiv.Plugin.ClipboardManager;

/// <summary>Connects the official clipboard guest to the native desktop clipboard.</summary>
public sealed class ClipboardManagerPlugin : IPlugin
{
    private static readonly string[] Commands = ["clear", "read_image", "read_text", "write_html", "write_image", "write_text"];
    public string Name => "clipboard-manager";

    public void Initialize(PluginContext context)
    {
        foreach (var command in Commands)
        {
            var name = command;
            context.RegisterCommand(name, async (invoke, args, token) =>
            {
                var result = await invoke.InvokePlatformAsync("plugin:clipboard-manager|" + name, args, token);
                if (result.IsBinary) throw new InvokeException("Clipboard operation returned unexpected binary data.");
                return result.Json;
            });
            context.Permission(new("allow-" + name.Replace('_', '-'), [name], []));
            context.Permission(new("deny-" + name.Replace('_', '-'), [], [name]));
        }
        context.PermissionSet("default");
    }
}
