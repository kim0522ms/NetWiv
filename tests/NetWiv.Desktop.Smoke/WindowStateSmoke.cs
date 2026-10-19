using System.Text.Json;
using NetWiv;
using NetWiv.Platform.Desktop;
using NetWiv.Plugin.WindowState;

internal static class WindowStateSmoke
{
    internal static int Run(string root, bool restore)
    {
        var plugin = new WindowStatePlugin();
        var identifier = "dev.netwiv.state." + Path.GetFileName(root);
        var failure = (Exception?)null;
        string? statePath = null;
        var runtime = new DesktopRuntime();
        using var app = new Builder().UseRuntime(runtime).Metadata(new("StateSmoke", "1.0", identifier))
            .Plugin(plugin).Window("main", "Window state smoke", "about:blank", 640, 480)
            .Setup(value => { Program.VerifyBackend(runtime); _ = Probe(value.Handle); }).Build();
        using var timeout = new Timer(_ => app.Handle.Exit(124), null, TimeSpan.FromSeconds(25), Timeout.InfiniteTimeSpan);
        app.Run();
        app.Dispose();
        if (failure is not null) throw failure;
        if (statePath is null || !File.Exists(statePath)) throw new Exception("Native shutdown did not save window state.");
        using var saved = JsonDocument.Parse(File.ReadAllText(statePath));
        var state = saved.RootElement.GetProperty("main");
        if (state.GetProperty("width").GetUInt32() != 910 || state.GetProperty("height").GetUInt32() != 650)
            throw new Exception("Window state lost the normal size while maximized: " + state.GetRawText());
        Console.WriteLine(restore ? "PASS native startup restores maximized state and normal bounds" : "PASS native shutdown persists normal bounds before destruction");
        if (restore) File.Delete(statePath);
        return 0;

        async Task Probe(AppHandle handle)
        {
            try
            {
                await plugin.Ready;
                statePath = await handle.ResolveDirectoryAsync((int)BaseDirectory.AppConfig, ".window-state.json");
                var window = handle.GetWindow("main")!;
                if (restore)
                {
                    await Wait(async () => await window.IsMaximizedAsync());
                    await window.UnmaximizeAsync();
                }
                else await window.SetSizeAsync(new PhysicalSize(910, 650));
                await Wait(async () => (await window.InnerSizeAsync()) is { Width: 910, Height: 650 });
                await window.MaximizeAsync();
                await Wait(async () => await window.IsMaximizedAsync());
            }
            catch (Exception error) { failure = error; }
            finally { handle.Exit(failure is null ? 0 : 1); }
        }
    }

    private static async Task Wait(Func<Task<bool>> condition)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!await condition()) await Task.Delay(30, stop.Token);
    }
}
