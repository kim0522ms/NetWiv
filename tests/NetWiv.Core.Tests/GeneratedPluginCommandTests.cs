using System.Text.Json;
using System.Text.Json.Serialization;
using NetWiv;

[assembly: CommandJsonContext(typeof(PluginCommandJson))]

internal static class GeneratedPluginCommandTests
{
    internal static async Task DispatchAndIsolation()
    {
        var runtime = new FakeRuntime
        {
            InvokeHandler = (command, _) => throw new InvokeException($"Command {command} not found")
        };
        using var app = new Builder().UseRuntime(runtime).Manage(new PluginCommandState(10))
            .Plugin(new FirstCommandPlugin()).Plugin(new SecondCommandPlugin())
            .RegisterGeneratedCommands()
            .Capability(new("plugins", ["generated-first:allow", "generated-second:allow"], Windows: ["main"]))
            .Build();
        app.Run();
        await Check("plugin:generated-first|get_status", "{\"step\":2}", "12");
        await Check("plugin:generated-second|get_status", "{}", "\"second\"");
        await Check("plugin:generated-first|renamed", "{}", "\"explicit\"");
        var missingArgument = await GuestIpc.Invoke(runtime, "plugin:generated-first|get_status", "{}");
        Assert.Equal("error", missingArgument.Headers!["Tauri-Response"]);
        Assert.Contains("step", GuestIpc.Body(missingArgument));
        var invalidArgument = await GuestIpc.Invoke(runtime, "plugin:generated-first|get_status", "{\"step\":\"wrong\"}");
        Assert.Equal("error", invalidArgument.Headers!["Tauri-Response"]);
        var failure = await GuestIpc.Invoke(runtime, "plugin:generated-first|fail", "{}");
        Assert.Equal("error", failure.Headers!["Tauri-Response"]);
        Assert.Contains("plugin failure", GuestIpc.Body(failure));

        // Permission alone must not expose a generated but unselected method.
        var hidden = await GuestIpc.Invoke(runtime, "plugin:generated-first|hidden", "{}");
        Assert.Equal("error", hidden.Headers!["Tauri-Response"]);
        Assert.Contains("not found", GuestIpc.Body(hidden));
        Assert.Throws<InvalidOperationException>(() => new Builder().UseRuntime(new FakeRuntime()).InvokeHandler("get_status").Build());
        var global = new CommandRegistry();
        GeneratedCommandRegistry.ApplyAllTo(global);
        Assert.True(!global.TryGet("get_status", out _), "Plugin commands leaked into the global registry.");
        Assert.True(!global.TryGet("plugin:generated-first|get_status", out _), "Plugin commands were registered globally.");

        async Task Check(string name, string payload, string expected)
        {
            var response = await GuestIpc.Invoke(runtime, name, payload);
            Assert.Equal("ok", response.Headers!["Tauri-Response"]);
            Assert.Equal(expected, GuestIpc.Body(response));
        }
    }

    internal static async Task Permissions()
    {
        foreach (var permissions in new string[][] { [], ["generated-first:allow", "generated-first:deny"] })
        {
            var runtime = new FakeRuntime();
            var state = new PluginCommandState(10);
            using var app = new Builder().UseRuntime(runtime).Manage(state)
                .Plugin(new FirstCommandPlugin())
                .Capability(new("plugins", permissions, Windows: ["main"]))
                .Build();
            app.Run();
            var denied = await GuestIpc.Invoke(runtime, "plugin:generated-first|get_status", "{\"step\":2}");
            Assert.Equal("error", denied.Headers!["Tauri-Response"]);
            Assert.Contains("not allowed", GuestIpc.Body(denied));
            Assert.Equal(0, state.Calls);
        }
    }

    internal static Task InvalidSelection()
    {
        var registry = new CommandRegistry();
        var context = new PluginContext("generated-first", registry, [], JsonValue.Null, typeof(FirstCommandPlugin));
        context.RegisterGeneratedCommands<FirstCommandPlugin>();
        Assert.Throws<InvalidOperationException>(() => context.RegisterGeneratedCommands<SecondCommandPlugin>("get_status"));
        Assert.Throws<ArgumentNullException>(() => context.RegisterGeneratedCommands<FirstCommandPlugin>(null!));
        Assert.Throws<ArgumentException>(() => context.RegisterGeneratedCommands<FirstCommandPlugin>(" "));
        Assert.Throws<InvalidOperationException>(() => context.RegisterGeneratedCommands<FirstCommandPlugin>("get_status", "missing"));
        Assert.True(!registry.TryGet("plugin:generated-first|get_status", out _), "Invalid selection partially registered commands.");
        Assert.Throws<InvalidOperationException>(() => context.RegisterGeneratedCommands<FirstCommandPlugin>("plugin:generated-first|get_status"));
        context.RegisterGeneratedCommands<FirstCommandPlugin>("get_status");
        Assert.Throws<InvalidOperationException>(() => context.RegisterGeneratedCommands<FirstCommandPlugin>("get_status"));
        Assert.Throws<InvalidOperationException>(() => GeneratedPluginCommandRegistry.Register<FirstCommandPlugin>("get_status", Handler));
        Assert.Throws<ArgumentException>(() => GeneratedPluginCommandRegistry.Register<FirstCommandPlugin>(" ", Handler));
        Assert.Throws<ArgumentException>(() => GeneratedPluginCommandRegistry.Register<FirstCommandPlugin>("other:status", Handler));
        Assert.Throws<ArgumentException>(() => GeneratedPluginCommandRegistry.Register<FirstCommandPlugin>("other|status", Handler));
        Assert.Throws<ArgumentNullException>(() => GeneratedPluginCommandRegistry.Register<FirstCommandPlugin>("unused", null!));
        return Task.CompletedTask;

        static ValueTask<JsonElement> Handler(InvokeContext context, JsonElement arguments, CancellationToken cancellationToken) => ValueTask.FromResult(JsonValue.Null);
    }
}

internal sealed class PluginCommandState(int offset)
{
    public int Offset { get; } = offset;
    public int Calls { get; set; }
}

internal sealed class FirstCommandPlugin : IPlugin
{
    public string Name => "generated-first";
    public void Initialize(PluginContext context)
    {
        context.RegisterGeneratedCommands<FirstCommandPlugin>("get_status", "renamed", "fail");
        context.Permission(new PermissionDefinition("allow", ["get_status", "renamed", "fail", "hidden"], []));
        context.Permission(new PermissionDefinition("deny", [], ["get_status"]));
    }

    [Command]
    public static async Task<int> GetStatus(int step, State<PluginCommandState> state, CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        state.Value.Calls++;
        return state.Value.Offset + step;
    }

    [Command("renamed")]
    public static string ExplicitName() => "explicit";

    [Command]
    public static void Hidden() => throw new InvalidOperationException("Unselected command executed.");

    [Command]
    public static async ValueTask Fail()
    {
        await Task.Yield();
        throw new InvalidOperationException("plugin failure");
    }
}

internal sealed class SecondCommandPlugin : IPlugin
{
    public string Name => "generated-second";
    public void Initialize(PluginContext context)
    {
        context.RegisterGeneratedCommands<SecondCommandPlugin>("get_status");
        context.Permission(new PermissionDefinition("allow", "get_status"));
    }
    [Command]
    public static string GetStatus() => "second";
}

[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(string))]
internal partial class PluginCommandJson : JsonSerializerContext;
