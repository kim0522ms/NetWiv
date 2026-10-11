using System.Text.Json;
using System.Text.Json.Serialization;
using NetWiv;

internal static class PluginConfigurationTests
{
    internal static async Task ValuesAndLifetime()
    {
        var configured = new ConfigPlugin("configured", true);
        var scalar = new ConfigPlugin("scalar", false);
        var absent = new ConfigPlugin("absent", false);
        var explicitNull = new ConfigPlugin("explicit-null", false);
        using var app = new Builder().UseRuntime(new FakeRuntime())
            .UseEmbeddedConfig(resourceName: "plugins-config.json")
            .Plugin(configured).Plugin(scalar).Plugin(absent).Plugin(explicitNull).Build();
        app.Run();
        Assert.Equal("한글 configuration", configured.Options!.Message);
        Assert.Equal(42, configured.Options.Count);
        Assert.Equal("[1, null, true]", scalar.Config.GetRawText());
        Assert.Equal(JsonValueKind.Null, absent.Config.ValueKind);
        Assert.Equal(JsonValueKind.Null, explicitNull.Config.ValueKind);
        // Deserialization happens after the embedded JsonDocument was disposed.
        Assert.Equal(42, (await app.Handle.InvokeAsync("plugin:configured|read", JsonValue.Null)).GetProperty("count").GetInt32());
        app.Dispose();
        Assert.Equal(1, configured.Disposals);
        Assert.Equal("한글 configuration", configured.Config.GetProperty("message").GetString());
    }

    internal static Task InvalidConfiguration()
    {
        Assert.Throws<InvalidOperationException>(() => new Builder().UseRuntime(new FakeRuntime())
            .UseEmbeddedConfig(resourceName: "plugins-invalid.json"));
        var first = new ConfigPlugin("scalar", false);
        var invalid = new ConfigPlugin("invalid", true);
        try
        {
            using var app = new Builder().UseRuntime(new FakeRuntime())
                .UseEmbeddedConfig(resourceName: "plugins-config.json").Plugin(first).Plugin(invalid).Build();
            throw new Exception("Invalid typed plugin configuration was accepted.");
        }
        catch (InvalidOperationException error)
        {
            Assert.Contains("Error deserializing 'plugins.invalid' within your Tauri configuration:", error.Message);
            Assert.True(error.InnerException is JsonException, "JSON error cause was lost.");
        }
        Assert.Equal(1, first.Disposals);
        Assert.Equal(1, invalid.Disposals);
        return Task.CompletedTask;
    }

    private sealed class ConfigPlugin(string name, bool typed) : IPlugin
    {
        public string Name => name;
        internal JsonElement Config;
        internal PluginOptions? Options;
        internal int Disposals;
        public void Initialize(PluginContext context)
        {
            Config = context.Config;
            if (typed) Options = context.ReadConfig(PluginOptionsJson.Default.PluginOptions);
            context.RegisterCommand("read", (_, _, _) => ValueTask.FromResult(Config));
        }
        public void Dispose() => Disposals++;
    }
}

internal sealed record PluginOptions(string Message, int Count);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PluginOptions))]
internal partial class PluginOptionsJson : JsonSerializerContext;
