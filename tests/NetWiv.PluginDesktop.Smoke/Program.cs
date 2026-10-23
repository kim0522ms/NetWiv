using System.Text.Json;
using NetWiv;
using NetWiv.Platform.Desktop;
using NetWiv.Plugin.ClipboardManager;
using NetWiv.Plugin.Dialog;
using NetWiv.Plugin.Notification;

internal static class Program
{
    private static bool _completed;

    [STAThread]
    private static int Main(string[] args)
    {
        var isolatedClipboard = args is ["--isolated-clipboard"];
        var dialogsNotifications = args is ["--dialogs-notifications"];
        if (args.Length != 0 && !isolatedClipboard && !dialogsNotifications) return 2;
        if (isolatedClipboard && (!OperatingSystem.IsLinux() ||
            Environment.GetEnvironmentVariable("NETWIV_TEST_ISOLATED_CLIPBOARD") != "1" ||
            Environment.GetEnvironmentVariable("GDK_BACKEND") != "x11" ||
            Environment.GetEnvironmentVariable("NETWIV_EXPECT_BACKEND") != "x11" ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY"))))
        {
            Console.Error.WriteLine("FAIL isolated clipboard mode requires Linux, its explicit test flag, and X11.");
            return 2;
        }

        string? root = null;
        var result = 1;
        try
        {
            var fixtureRoot = Directory.CreateTempSubdirectory("netwiv-plugin-smoke-").FullName;
            root = fixtureRoot;
            File.WriteAllText(Path.Combine(fixtureRoot, "selection.txt"), "synthetic dialog content");

            var runtime = new DesktopRuntime();
            var builder = new Builder().UseRuntime(runtime)
                .Plugin(new ClipboardManagerPlugin())
                .Plugin(new DialogPlugin())
                .Plugin(new NotificationPlugin())
                .Metadata(new AppMetadata("NetWiv Plugin Smoke", "1.0.0", "dev.netwiv.plugin-smoke"))
                .Permission(new("allow-smoke-fixture", "smoke_fixture"))
                .Permission(new("allow-smoke-selection", "smoke_validate_selection"))
                .Permission(new("allow-smoke-finish", "smoke_finish"))
                .Capability(new("desktop-smoke", [
                    "allow-smoke-fixture", "allow-smoke-selection", "allow-smoke-finish",
                    "clipboard-manager:allow-read-text", "clipboard-manager:allow-write-text",
                    "dialog:allow-open", "notification:allow-notify"
                ], Windows: ["main"]))
                .RegisterCommand("smoke_fixture", (_, _, _) => Value(JsonValue.Parse("{\"root\":" +
                    JsonValue.From(fixtureRoot).GetRawText() + ",\"clipboardIncluded\":" +
                    (dialogsNotifications ? "false" : "true") + "}")))
                .RegisterCommand("smoke_validate_selection", (_, args, _) =>
                {
                    var selected = args.TryGetProperty("selected", out var value) && value.ValueKind == JsonValueKind.String
                        ? value.GetString() : null;
                    return Value(JsonValue.From(selected is not null && IsSyntheticSelection(fixtureRoot, selected)));
                })
                .RegisterCommand("smoke_finish", (_, args, _) =>
                {
                    if (!args.TryGetProperty("complete", out var complete) || complete.ValueKind != JsonValueKind.True)
                        throw new InvokeException("Desktop plugin smoke checks are incomplete.");
                    _completed = true;
                    return Value(JsonValue.Null);
                })
                .UseEmbeddedAssets()
                .UseEmbeddedConfig()
                .Window("main", "NetWiv plugin smoke", "index.html", 640, 620)
                .Setup(app =>
                {
                    if (!isolatedClipboard) return;
                    if (runtime.Backend != "x11")
                    {
                        Console.Error.WriteLine("FAIL isolated clipboard mode did not start on X11.");
                        app.Handle.Exit(1);
                        return;
                    }
                    _ = Task.Run(() => RunIsolatedClipboard(app.Handle));
                });

            using (var app = builder.Build()) app.Run();
            result = _completed ? 0 : 1;
        }
        catch
        {
            Console.Error.WriteLine("FAIL desktop plugin smoke host.");
        }
        finally
        {
            if (root is not null)
            {
                try { Directory.Delete(root, recursive: true); }
                catch { Console.Error.WriteLine("FAIL synthetic dialog fixture cleanup."); result = 1; }
            }
        }
        return result;
    }

    private static bool IsSyntheticSelection(string root, string selected)
    {
        try
        {
            var expected = ResolvePath(Path.Combine(root, "selection.txt"));
            var actual = ResolvePath(selected);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return string.Equals(expected, actual, comparison);
        }
        catch { return false; }
    }

    private static string ResolvePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var volume = Path.GetPathRoot(fullPath)!;
        var current = volume;
        var relative = Path.GetRelativePath(volume, fullPath);
        foreach (var component in relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            current = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? current;
        }
        return Path.GetFullPath(current);
    }

    private static async Task RunIsolatedClipboard(AppHandle app)
    {
        var result = 1;
        var mutationAttempted = false;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            const string marker = "synthetic isolated X11 clipboard smoke";
            mutationAttempted = true;
            await app.InvokeAsync("plugin:clipboard-manager|write_text", JsonValue.Parse("""{"text":"synthetic isolated X11 clipboard smoke"}"""), timeout.Token)
                .AsTask().WaitAsync(timeout.Token);
            var read = await app.InvokeAsync("plugin:clipboard-manager|read_text", JsonValue.EmptyObject, timeout.Token)
                .AsTask().WaitAsync(timeout.Token);
            if (read.ValueKind != JsonValueKind.String || !string.Equals(read.GetString(), marker, StringComparison.Ordinal))
                throw new InvalidOperationException();
            result = 0;
        }
        catch { }
        finally
        {
            if (mutationAttempted)
            {
                try
                {
                    using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await app.InvokeAsync("plugin:clipboard-manager|clear", JsonValue.EmptyObject, cleanupTimeout.Token)
                        .AsTask().WaitAsync(cleanupTimeout.Token);
                }
                catch { result = 1; }
            }
            Console.WriteLine(result == 0 ? "PASS isolated X11 clipboard write/read/clear." : "FAIL isolated X11 clipboard smoke.");
            _completed = result == 0;
            app.Exit(result);
        }
    }

    private static ValueTask<JsonElement> Value(JsonElement value) => ValueTask.FromResult(value);
}
