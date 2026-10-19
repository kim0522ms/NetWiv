using System.Collections.Concurrent;
using System.Text.Json;
using NetWiv;
using NetWiv.Platform.Desktop;
using NetWiv.Plugin.Dialog;
using NetWiv.Plugin.Fs;
using NetWiv.Runtime;

internal static class Program
{
    private static readonly ConcurrentDictionary<string, TaskCompletionSource<RuntimeCommandContext>> Documents = new();
    private static readonly string[] Kinds = ["file", "files", "folder", "folders", "save", "message"];
    private static DesktopRuntime _runtime = null!;
    private static string _root = "", _backend = "", _phase = "startup";
    private static bool _interactive, _denied;
    private static int _result = 1, _verified;
    private static Task<JsonElement>? _shutdown;

    [STAThread]
    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsLinux() || args.Length != 2 || args[0] is not ("--automatic" or "--interactive") || args[1] is not ("x11" or "wayland"))
        { Console.Error.WriteLine("usage: NetWiv.Dialog.Smoke --automatic|--interactive x11|wayland (Linux desktop)"); return 2; }
        _interactive = args[0] == "--interactive"; _backend = args[1];
        using var watchdog = new Timer(_ => { Console.Error.WriteLine("FAIL dialog probe timed out."); Environment.Exit(124); }, null,
            TimeSpan.FromMinutes(_interactive ? 10 : 2), Timeout.InfiniteTimeSpan);
        try
        {
            _root = Directory.CreateTempSubdirectory("netwiv-dialog-").FullName;
            foreach (var directory in new[] { "folder-one/nested", "folder-two" }) Directory.CreateDirectory(Path.Combine(_root, directory));
            foreach (var file in new[] { "alpha.txt", "beta.txt", "folder-one/nested/inside.txt" }) File.WriteAllText(Path.Combine(_root, file), "synthetic dialog sample");
            _runtime = new DesktopRuntime();
            using var app = new Builder().UseRuntime(_runtime).Plugin(new FsPlugin()).Plugin(new DialogPlugin())
                .UseEmbeddedAssets().UseEmbeddedConfig()
                .Window("main", "NetWiv dialog probe", "index.html", 800, 600)
                .Window("denied", "Denied dialog capability", "index.html", 400, 200, JsonValue.Parse("""{"visible":false}"""))
                .Permission(new("allow-probe", "probe_ready", "probe_verify", "probe_finish", "probe_denied", "probe_interactive_ready"))
                .Capability(new("probe", ["allow-probe"], Windows: ["main", "denied", "lifecycle"]))
                .Capability(new("dialogs", ["dialog:allow-open", "dialog:allow-save", "dialog:allow-message", "fs:allow-read-text-file"], Windows: ["main"]))
                .Capability(new("deny-dialogs", ["dialog:allow-open", "dialog:deny-open"], Windows: ["denied"]))
                .RegisterCommand("probe_ready", Ready)
                .RegisterCommand("probe_interactive_ready", (context, _, _) =>
                {
                    Require(_interactive && context.WebviewLabel == "main", "interactive source");
                    Console.WriteLine("READY native dialog interactive probe; follow the synthetic selection steps in the window.");
                    return Value(JsonValue.Null);
                })
                .RegisterCommand("probe_denied", async (context, _, _) =>
                {
                    Require(context.WebviewLabel == "denied", "denied source"); _denied = true;
                    await context.App.GetWindow("denied")!.DestroyAsync(); return JsonValue.Null;
                })
                .RegisterCommand("probe_verify", Verify)
                .RegisterCommand("probe_finish", (context, value, _) =>
                {
                    Require(_interactive && _verified == 8 && _denied && value.GetProperty("visual").GetBoolean(), "interactive completion");
                    _result = 0; context.App.Exit(); return Value(JsonValue.Null);
                })
                .Setup(value =>
                {
                    Require(_runtime.Backend == _backend, "native backend mismatch");
                    Console.WriteLine($"BACKEND {_runtime.Backend}");
                    if (!_interactive) _ = Automatic(value.Handle);
                }).Build();
            app.Run();
            if (_shutdown is not null) ExpectStopped(_shutdown).GetAwaiter().GetResult();
            if (_result == 0) Console.WriteLine(_interactive ? "PASS native dialog selections, scope grants and explicit visual confirmation." : "PASS native dialog responsiveness, cancellation and lifecycle cleanup.");
        }
        catch (Exception) { Console.Error.WriteLine($"FAIL dialog probe during {_phase}."); _result = 1; }
        finally
        {
            try { if (_root.Length != 0) Directory.Delete(_root, recursive: true); }
            catch (Exception) { Console.Error.WriteLine("FAIL synthetic dialog sample cleanup."); _result = 1; }
        }
        return _result;
    }

    private static ValueTask<JsonElement> Ready(InvokeContext context, JsonElement args, CancellationToken token)
    {
        Require(_runtime.Backend == _backend && context.DocumentId != 0, "guest backend/document");
        Document(context.WebviewLabel).TrySetResult(new(context.WebviewLabel, context.Source, true, context.WindowLabel, DocumentId: context.DocumentId));
        return Value(Write(writer =>
        {
            writer.WriteStartObject(); writer.WriteString("root", _root); writer.WriteBoolean("interactive", _interactive);
            writer.WriteBoolean("denied", context.WebviewLabel == "denied"); writer.WriteEndObject();
        }));
    }

    private static async Task Automatic(AppHandle app)
    {
        try
        {
            await Document("main").Task.WaitAsync(TimeSpan.FromSeconds(10));
            await WaitUntil(() => _denied);
            var window = app.GetWindow("main")!;
            using (var canceled = new CancellationTokenSource())
            {
                canceled.Cancel();
                var rejected = window.InvokeAsync("plugin:dialog|open", Arguments("file"), canceled.Token);
                try { await rejected; throw new InvalidOperationException("pre-canceled dialog returned normally"); }
                catch (OperationCanceledException) when (canceled.IsCancellationRequested) { }
                Require(rejected.IsCanceled, "pre-canceled dialog task state");
                Console.WriteLine("PASS pre-canceled dialog request.");
            }
            foreach (var kind in Kinds) { _phase = kind; await Cancel(window, app, kind); Console.WriteLine($"PASS canceled native {kind} and responsive UI/IPC."); }
            foreach (var change in new[] { "parent close", "webview close", "document reload" })
            {
                _phase = change; Documents.TryRemove("lifecycle", out _);
                var parent = await new WindowBuilder(app, "lifecycle").Title("Dialog lifecycle probe").BuildAsync();
                var view = await new WebviewBuilder(app, parent, "lifecycle").Url("index.html").BuildAsync();
                var source = await Document("lifecycle").Task.WaitAsync(TimeSpan.FromSeconds(10));
                var pending = _runtime.InvokeAsync("plugin:dialog|open", Arguments("file"), source);
                await Responsive(app, pending);
                if (change == "parent close") await parent.DestroyAsync();
                else if (change == "webview close") await view.CloseAsync();
                else { Documents.TryRemove("lifecycle", out _); view.EvaluateScript("location.reload()"); }
                await ExpectInvalidated(pending, change switch
                {
                    "parent close" => "dialog parent window was closed",
                    "webview close" => "dialog source webview was closed",
                    _ => "dialog source document changed"
                });
                if (change == "document reload") Require((await Document("lifecycle").Task.WaitAsync(TimeSpan.FromSeconds(10))).DocumentId != source.DocumentId, "new document generation");
                if (change != "parent close") await parent.DestroyAsync();
                await Cancel(window, app, "file");
                Console.WriteLine($"PASS pending dialog {change} and subsequent open.");
            }
            _phase = "app shutdown";
            _shutdown = window.InvokeAsync("plugin:dialog|open", Arguments("file"));
            await Responsive(app, _shutdown); _result = 0; app.Exit();
        }
        catch (Exception) { Console.Error.WriteLine($"FAIL dialog probe during {_phase}."); _result = 1; app.Exit(1); }
    }

    private static async Task Cancel(Window window, AppHandle app, string kind)
    {
        using var cancel = new CancellationTokenSource();
        var pending = window.InvokeAsync("plugin:dialog|" + (kind is "save" or "message" ? kind : "open"), Arguments(kind), cancel.Token);
        await Responsive(app, pending); cancel.Cancel();
        try { await pending.WaitAsync(TimeSpan.FromSeconds(3)); throw new InvalidOperationException("dialog cancellation returned normally"); }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        Require(pending.IsCanceled, "dialog task cancellation state");
    }

    private static async Task Responsive(AppHandle app, Task pending)
    {
        await Task.Delay(300); Require(!pending.IsCompleted, "dialog was not pending");
        var dispatch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        app.Dispatch(() => dispatch.SetResult());
        await dispatch.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Require(await app.GetWindow("main")!.TitleAsync().WaitAsync(TimeSpan.FromSeconds(2)) == "NetWiv dialog probe", "parallel native query");
        Require(!pending.IsCompleted, "dialog completed before cancellation");
    }

    private static async Task ExpectInvalidated(Task pending, string expected)
    {
        try { await pending.WaitAsync(TimeSpan.FromSeconds(3)); throw new InvalidOperationException("invalidated dialog returned normally"); }
        catch (InvokeException error) when (error.Message == expected) { }
    }

    private static async Task ExpectStopped(Task pending)
    {
        try { await pending.WaitAsync(TimeSpan.FromSeconds(3)); throw new InvalidOperationException("shutdown dialog returned normally"); }
        catch (InvokeException) { }
        catch (OperationCanceledException) { }
    }

    private static JsonElement Arguments(string kind) => Write(writer =>
    {
        writer.WriteStartObject();
        if (kind == "message") { writer.WriteString("message", "Synthetic dialog probe: choose Accept."); writer.WriteString("title", "NetWiv message probe"); writer.WriteString("buttons", "OkCancel"); }
        else
        {
            writer.WriteStartObject("options"); writer.WriteString("title", "NetWiv " + kind + " probe");
            writer.WriteString("defaultPath", kind == "save" ? Path.Combine(_root, "saved.txt") : _root);
            writer.WriteBoolean("directory", kind is "folder" or "folders"); writer.WriteBoolean("multiple", kind is "files" or "folders"); writer.WriteEndObject();
        }
        writer.WriteEndObject();
    });

    private static ValueTask<JsonElement> Verify(InvokeContext context, JsonElement args, CancellationToken token)
    {
        Require(_interactive && context.WebviewLabel == "main" && args.GetProperty("step").GetInt32() == _verified, "selection sequence");
        var value = args.GetProperty("selected");
        string[] names = _verified switch { 0 or 7 => ["alpha.txt"], 1 => ["alpha.txt", "beta.txt"], 2 => ["folder-one"], 3 => ["folder-one", "folder-two"], 4 => ["saved.txt"], _ => [] };
        if (_verified == 5) Require(value.GetString() == "Accept", "message custom button result");
        else if (_verified == 6) Require(value.ValueKind == JsonValueKind.Null, "user cancel result");
        else
        {
            var paths = value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Select(item => item.GetString()).ToArray() : [value.GetString()];
            Require(paths.Length == names.Length && paths.Order().SequenceEqual(names.Select(name => Path.Combine(_root, name)).Order()), "synthetic selection/cardinality");
            Require(paths.All(path => path is not null && context.App.AssetProtocolScope().IsAllowed(path)), "selected asset scope grant");
            if (_verified == 2) Require(context.App.AssetProtocolScope().IsAllowed(Path.Combine(_root, "folder-one/nested/inside.txt")), "recursive asset grant");
        }
        Console.WriteLine($"PASS interactive dialog step {_verified + 1}."); _verified++; return Value(JsonValue.Null);
    }

    private static TaskCompletionSource<RuntimeCommandContext> Document(string label) => Documents.GetOrAdd(label, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
    private static async Task WaitUntil(Func<bool> condition) { using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10)); while (!condition()) await Task.Delay(25, stop.Token); }
    private static ValueTask<JsonElement> Value(JsonElement value) => ValueTask.FromResult(value);
    private static JsonElement Write(Action<Utf8JsonWriter> write)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) { write(writer); writer.Flush(); }
        using var document = JsonDocument.Parse(buffer.ToArray());
        return document.RootElement.Clone();
    }
    private static void Require(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); }
}
