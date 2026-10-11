using NetWiv;
using NetWiv.Plugin.GlobalShortcut;

internal static class GlobalShortcutPluginTests
{
    internal static async Task GuestCommandsAndChannel()
    {
        var calls = new List<string>();
        var runtime = new FakeRuntime
        {
            InvokeHandler = (command, args) =>
            {
                calls.Add(command);
                return command switch
                {
                    "plugin:global-shortcut|register" => JsonValue.Parse("[41,42]"),
                    "plugin:global-shortcut|unregister" => JsonValue.Parse("[41]"),
                    "plugin:global-shortcut|is_registered" => JsonValue.From(true),
                    _ => JsonValue.Null
                };
            }
        };
        using var app = new Builder().UseRuntime(runtime).Plugin(new GlobalShortcutPlugin()).Build();
        app.Run();
        await app.Handle.InvokeAsync("plugin:global-shortcut|register",
            JsonValue.Parse("""{"shortcuts":["Ctrl+Alt+G","Ctrl+Alt+H"],"handler":"__CHANNEL__:23"}"""));
        Assert.Equal("plugin:global-shortcut|register", calls[0]);
        runtime.Context!.OnEvent(new("netwiv://global-shortcut", null,
            JsonValue.Parse("""{"shortcut":"Ctrl+Alt+G","id":41,"state":"Pressed"}""")));
        Assert.Contains("runCallback(23,", await runtime.NextScript());
        Assert.True((await app.Handle.InvokeAsync("plugin:global-shortcut|is_registered",
            JsonValue.Parse("""{"shortcut":"Ctrl+Alt+G"}"""))).GetBoolean(), "Native registration was not queried.");
        await app.Handle.InvokeAsync("plugin:global-shortcut|unregister",
            JsonValue.Parse("""{"shortcuts":["Ctrl+Alt+G"]}"""));
        runtime.Context.OnEvent(new("netwiv://global-shortcut", null,
            JsonValue.Parse("""{"shortcut":"Ctrl+Alt+G","id":41,"state":"Released"}""")));
        Assert.Equal(0, runtime.PendingScripts);
        await app.Handle.InvokeAsync("plugin:global-shortcut|unregister_all", JsonValue.EmptyObject);
        Assert.Contains("\"end\":true", await runtime.NextScript());
        Assert.Equal("plugin:global-shortcut|unregister_all", calls[^1]);
        try
        {
            await app.Handle.InvokeAsync("plugin:global-shortcut|register",
                JsonValue.Parse("""{"shortcuts":["Ctrl+G"],"handler":"bad"}"""));
            throw new Exception("Invalid channel was accepted.");
        }
        catch (InvokeException) { }
    }
}
