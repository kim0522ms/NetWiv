using NetWiv;
using NetWiv.Plugin.Cli;

internal static class CliPluginTests
{
    internal static async Task MatchesAndErrors()
    {
        using var app = new Builder().UseRuntime(new FakeRuntime())
            .UseEmbeddedConfig(resourceName: "plugins-cli.json")
            .Plugin(new CliPlugin(["-vv", "--type", "foo", "bar", "run", "--debug"])).Build();
        var result = await app.Handle.InvokeAsync("plugin:cli|cli_matches", JsonValue.EmptyObject);
        var args = result.GetProperty("args");
        Assert.True(args.GetProperty("verbose").GetProperty("value").GetBoolean(), "Verbose flag was not matched.");
        Assert.Equal(2, args.GetProperty("verbose").GetProperty("occurrences").GetInt32());
        Assert.Equal("foo", args.GetProperty("type").GetProperty("value")[0].GetString());
        Assert.Equal("bar", args.GetProperty("type").GetProperty("value")[1].GetString());
        Assert.Equal("run", result.GetProperty("subcommand").GetProperty("name").GetString());
        Assert.True(result.GetProperty("subcommand").GetProperty("matches").GetProperty("args")
            .GetProperty("debug").GetProperty("value").GetBoolean(), "Subcommand flag was not matched.");

        using var invalid = new Builder().UseRuntime(new FakeRuntime())
            .UseEmbeddedConfig(resourceName: "plugins-cli.json")
            .Plugin(new CliPlugin(["--type", "not-allowed"])).Build();
        try
        {
            await invalid.Handle.InvokeAsync("plugin:cli|cli_matches", JsonValue.EmptyObject);
            throw new Exception("An invalid configured CLI value was accepted.");
        }
        catch (InvokeException) { }
    }
}
