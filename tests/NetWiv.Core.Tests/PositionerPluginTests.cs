using System.Text.Json;
using NetWiv;
using NetWiv.Plugin.Positioner;

internal static class PositionerPluginTests
{
    internal static async Task AnchorsAndTray()
    {
        JsonElement? moved = null;
        var runtime = new FakeRuntime
        {
            InvokeHandler = (command, args) => command switch
            {
                "plugin:window|current_monitor" or "plugin:window|monitor_from_point" =>
                    JsonValue.Parse("{\"name\":\"test\",\"position\":{\"x\":100,\"y\":200},\"size\":{\"width\":1000,\"height\":700},\"workArea\":{\"position\":{\"x\":100,\"y\":200},\"size\":{\"width\":1000,\"height\":700}},\"scaleFactor\":1}"),
                "plugin:window|outer_size" => JsonValue.Parse("{\"width\":200,\"height\":100}"),
                "plugin:window|set_position" => Capture(args),
                _ => JsonValue.Null
            }
        };
        using var app = new Builder().UseRuntime(runtime).Window("main", "Main", "index.html")
            .Plugin(new PositionerPlugin()).Build();
        app.Run();

        JsonElement Capture(JsonElement args) { moved = args.Clone(); return JsonValue.Null; }
        async Task Check(int anchor, int x, int y, bool constrained = false)
        {
            moved = null;
            var command = constrained ? "move_window_constrained" : "move_window";
            await app.Handle.InvokeForTargetAsync("plugin:positioner|" + command,
                JsonValue.Parse("{\"position\":" + anchor + "}"), "main", CancellationToken.None);
            var position = moved!.Value.GetProperty("value").GetProperty("Physical");
            Assert.Equal(x, position.GetProperty("x").GetInt32());
            Assert.Equal(y, position.GetProperty("y").GetInt32());
        }

        await Check(0, 100, 200);
        await Check(3, 900, 800);
        await Check(8, 500, 500);
        try
        {
            await Check(9, 0, 0);
            throw new Exception("A tray anchor was accepted without an icon position.");
        }
        catch (InvokeException) { }
        await app.Handle.InvokeAsync("plugin:positioner|set_tray_icon_state",
            JsonValue.Parse("{\"position\":{\"x\":1050,\"y\":250},\"size\":{\"width\":20,\"height\":20}}"));
        await Check(14, 960, 250);
        await Check(14, 900, 250, constrained: true);
    }
}
