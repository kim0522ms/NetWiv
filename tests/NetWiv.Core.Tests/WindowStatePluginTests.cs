using System.Text.Json;
using NetWiv;
using NetWiv.Plugin.WindowState;

internal static class WindowStatePluginTests
{
    internal static async Task AutomaticLifecycle()
    {
        var identifier = "dev.netwiv.windowstate." + Guid.NewGuid().ToString("N");
        const string snapshot = """{"width":912,"height":654,"x":32,"y":48,"maximized":false,"visible":true,"decorated":true,"fullscreen":false}""";
        string path;
        var firstRuntime = new FakeRuntime();
        using (var first = new Builder().UseRuntime(firstRuntime).Metadata(new("StateProbe", "1.0", identifier))
            .Window("main", "Main", "index.html").Plugin(new WindowStatePlugin()).Build())
        {
            path = await first.Handle.ResolveDirectoryAsync((int)BaseDirectory.AppConfig, ".window-state.json");
            first.Run();
            firstRuntime.Context!.OnEvent(new("netwiv://window-state", "main", JsonValue.Parse(snapshot), new("Window", "main")));
            firstRuntime.Context.OnEvent(new("tauri://destroyed", "main", JsonValue.Null, new("Window", "main")));
        }
        try
        {
            Assert.True(File.Exists(path), "Shutdown did not persist the final native snapshot.");
            using var saved = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal(912, saved.RootElement.GetProperty("main").GetProperty("width").GetInt32());
            var restored = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondRuntime = new FakeRuntime { InvokeHandler = (command, args) =>
            {
                if (command == "plugin:window|set_size") restored.TrySetResult(args.Clone());
                return command == "plugin:window|available_monitors" ? JsonValue.Parse("[]") : JsonValue.Null;
            } };
            using var second = new Builder().UseRuntime(secondRuntime).Metadata(new("StateProbe", "1.0", identifier))
                .Window("main", "Main", "index.html").Plugin(new WindowStatePlugin()).Build();
            second.Run();
            var size = await restored.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Contains("912", size.GetRawText());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    internal static async Task AutomaticPolicy()
    {
        const string initial = """{"main":{"width":700,"height":500,"x":15,"y":20,"maximized":false,"visible":true,"decorated":true,"fullscreen":false}}""";
        const string final = """{"width":912,"height":654,"x":100,"y":200,"maximized":true,"visible":false,"decorated":false,"fullscreen":true}""";
        var identifier = "dev.netwiv.statepolicy." + Guid.NewGuid().ToString("N");
        using var resolver = new Builder().UseRuntime(new FakeRuntime()).Metadata(new("State", "1.0", identifier)).Build();
        var path = await resolver.Handle.ResolveDirectoryAsync((int)BaseDirectory.AppConfig, ".window-state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            foreach (var automatic in new[] { true, false })
            {
                File.WriteAllText(path, initial);
                var runtime = new FakeRuntime();
                using (var app = new Builder().UseRuntime(runtime).Metadata(new("State", "1.0", identifier)).Window("main", "Main", "index.html")
                    .Plugin(new WindowStatePlugin(".window-state.json", StateFlags.Size, automatic)).Build())
                {
                    app.Run();
                    runtime.Context!.OnEvent(new("netwiv://window-state", "main", JsonValue.Parse(final), new("Window", "main")));
                }
                using var saved = JsonDocument.Parse(File.ReadAllText(path));
                var state = saved.RootElement.GetProperty("main");
                Assert.Equal(automatic ? 912 : 700, state.GetProperty("width").GetInt32());
                Assert.Equal(15, state.GetProperty("x").GetInt32());
                Assert.True(!state.GetProperty("maximized").GetBoolean(), "Unselected state flag was overwritten.");
            }
            File.WriteAllText(path, "broken json");
            var corruptRuntime = new FakeRuntime();
            using (var app = new Builder().UseRuntime(corruptRuntime).Metadata(new("State", "1.0", identifier))
                .Window("main", "Main", "index.html").Plugin(new WindowStatePlugin()).Build())
            {
                app.Run();
                corruptRuntime.Context!.OnEvent(new("netwiv://window-state", "main", JsonValue.Parse(final), new("Window", "main")));
            }
            Assert.Equal("broken json", File.ReadAllText(path));
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    internal static async Task SaveAndRestore()
    {
        var filename = "netwiv-window-state-test-" + Guid.NewGuid().ToString("N") + ".json";
        var calls = new List<string>();
        var runtime = new FakeRuntime
        {
            InvokeHandler = (command, _) =>
            {
                calls.Add(command);
                return command switch
                {
                    "plugin:window|is_maximized" or "plugin:window|is_minimized" or "plugin:window|is_fullscreen" => JsonValue.From(false),
                    "plugin:window|is_visible" or "plugin:window|is_decorated" => JsonValue.From(true),
                    "plugin:window|inner_size" => JsonValue.Parse("{\"width\":800,\"height\":600}"),
                    "plugin:window|outer_position" => JsonValue.Parse("{\"x\":32,\"y\":48}"),
                    "plugin:window|available_monitors" => JsonValue.Parse("[{\"name\":\"test\",\"size\":{\"width\":1920,\"height\":1080},\"position\":{\"x\":0,\"y\":0},\"workArea\":{\"position\":{\"x\":0,\"y\":0},\"size\":{\"width\":1920,\"height\":1080}},\"scaleFactor\":1}]"),
                    _ => JsonValue.Null
                };
            }
        };
        using var app = new Builder().UseRuntime(runtime).Window("main", "Main", "index.html")
            .Plugin(new WindowStatePlugin(filename)).Build();
        var path = await app.Handle.ResolveDirectoryAsync((int)BaseDirectory.AppConfig, filename);
        try
        {
            Assert.Equal(filename, (await app.Handle.InvokeAsync("plugin:window-state|filename", JsonValue.Parse("{}"))).GetString());
            await app.Handle.InvokeAsync("plugin:window-state|save_window_state", JsonValue.Parse("{}"));
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var saved = document.RootElement.GetProperty("main");
            Assert.Equal(800, saved.GetProperty("width").GetInt32());
            Assert.Equal(48, saved.GetProperty("y").GetInt32());
            calls.Clear();
            await app.Handle.InvokeAsync("plugin:window-state|restore_state", JsonValue.Parse("{\"label\":\"main\",\"flags\":3}"));
            Assert.True(calls.Contains("plugin:window|set_size"), "Size was not restored.");
            Assert.True(calls.Contains("plugin:window|set_position"), "Position was not restored.");
            Assert.True(!calls.Contains("plugin:window|maximize"), "An unselected flag was restored.");
            try
            {
                await app.Handle.InvokeAsync("plugin:window-state|restore_state", JsonValue.Parse("{\"label\":\"unknown\"}"));
                throw new Exception("Unknown window was accepted.");
            }
            catch (InvokeException) { }
        }
        finally { File.Delete(path); }
    }
}
