using System.Text.Json;

namespace NetWiv.Plugin.WindowState;

[Flags]
public enum StateFlags { Size = 1, Position = 2, Maximized = 4, Visible = 8, Decorations = 16, Fullscreen = 32, All = 63 }

/// <summary>Persists and restores the official window-state guest's desktop window properties.</summary>
public sealed class WindowStatePlugin : IPlugin
{
    private readonly string _filename;
    private readonly StateFlags _defaultFlags;
    private readonly SemaphoreSlim _fileGate = new(1, 1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, WindowSnapshot> _finalSnapshots = new(StringComparer.Ordinal);
    private readonly bool _automatic;
    private readonly CancellationTokenSource _stop = new();
    private string? _path;
    private int _disposed;

    /// <summary>Completes after startup restoration has been attempted. Never block the UI thread on this task.</summary>
    public Task Ready { get; private set; } = Task.CompletedTask;

    public WindowStatePlugin(string filename = ".window-state.json", StateFlags stateFlags = StateFlags.All)
        : this(filename, stateFlags, true) { }

    public WindowStatePlugin(string filename, StateFlags stateFlags, bool automatic)
    {
        if (string.IsNullOrWhiteSpace(filename) || Path.GetFileName(filename) != filename || filename is "." or "..")
            throw new ArgumentException("The window state filename must be a file name inside the app config directory.", nameof(filename));
        if ((stateFlags & ~StateFlags.All) != 0) throw new ArgumentOutOfRangeException(nameof(stateFlags));
        _filename = filename;
        _defaultFlags = stateFlags;
        _automatic = automatic;
    }

    public string Name => "window-state";

    public void Setup(AppHandle app)
    {
        if (_automatic) Ready = RestoreStartupAsync(app);
    }

    private async Task RestoreStartupAsync(AppHandle app)
    {
        try
        {
            _path = await PathAsync(app, _stop.Token);
            foreach (var window in app.Windows) await RestoreAutomaticallyAsync(app, window.Label);
        }
        catch (Exception error) { Console.Error.WriteLine($"Window state startup restore failed: {error.Message}"); }
    }

    private async Task RestoreAutomaticallyAsync(AppHandle app, string label)
    {
        try { await RestoreAsync(app, label, _defaultFlags, _stop.Token); }
        catch (Exception) when (Volatile.Read(ref _disposed) != 0 || app.GetWindow(label) is null) { }
        catch (Exception error) { Console.Error.WriteLine($"Window state restore failed for '{label}': {error.Message}"); }
    }

    public void OnEvent(AppHandle app, Runtime.RuntimeEvent runtimeEvent)
    {
        if (!_automatic || Volatile.Read(ref _disposed) != 0) return;
        if (runtimeEvent.EventName == "tauri://window-created" && _path is not null && runtimeEvent.Label is { } created)
            _ = RestoreAutomaticallyAsync(app, created);
        // This backend snapshot is emitted before native window destruction.
        // It already contains the last non-minimized/non-maximized bounds.
        if (runtimeEvent.EventName == "netwiv://window-state" && runtimeEvent.Label is { } label)
            _finalSnapshots[label] = ReadSnapshot(runtimeEvent.Payload);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        _stop.Dispose();
        if (!_automatic || _path is null || _finalSnapshots.IsEmpty) return;
        _fileGate.Wait();
        try
        {
            var states = ReadStates(_path);
            foreach (var (label, snapshot) in _finalSnapshots)
            {
                if (!states.TryGetValue(label, out var state)) state = new() { Visible = true, Decorated = true };
                if (_defaultFlags.HasFlag(StateFlags.Size)) { state.Width = snapshot.Width; state.Height = snapshot.Height; }
                if (_defaultFlags.HasFlag(StateFlags.Position)) { state.X = snapshot.X; state.Y = snapshot.Y; }
                if (_defaultFlags.HasFlag(StateFlags.Maximized)) state.Maximized = snapshot.Maximized;
                if (_defaultFlags.HasFlag(StateFlags.Visible)) state.Visible = snapshot.Visible;
                if (_defaultFlags.HasFlag(StateFlags.Decorations)) state.Decorated = snapshot.Decorated;
                if (_defaultFlags.HasFlag(StateFlags.Fullscreen)) state.Fullscreen = snapshot.Fullscreen;
                states[label] = state;
            }
            WriteStatesAsync(_path, states, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception error) { Console.Error.WriteLine($"Window state shutdown save failed: {error.Message}"); }
        finally { _fileGate.Release(); }
    }

    public void Initialize(PluginContext context)
    {
        context.RegisterCommand("filename", (_, _, _) => ValueTask.FromResult(JsonValue.From(_filename)));
        context.RegisterCommand("save_window_state", async (invoke, args, token) =>
        {
            await SaveAsync(invoke.App, Flags(args), token);
            return JsonValue.Null;
        });
        context.RegisterCommand("restore_state", async (invoke, args, token) =>
        {
            if (!args.TryGetProperty("label", out var label) || label.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(label.GetString()))
                throw new InvokeException("Window label is required.");
            await RestoreAsync(invoke.App, label.GetString()!, Flags(args), token);
            return JsonValue.Null;
        });
        foreach (var command in new[] { "filename", "save_window_state", "restore_state" })
        {
            var permission = command.Replace('_', '-');
            context.Permission(new("allow-" + permission, [command], []));
            context.Permission(new("deny-" + permission, [], [command]));
        }
        context.PermissionSet("default", "allow-filename", "allow-save-window-state", "allow-restore-state");
    }

    private StateFlags Flags(JsonElement args)
    {
        if (!args.TryGetProperty("flags", out var value) || value.ValueKind == JsonValueKind.Null) return _defaultFlags;
        if (!value.TryGetInt32(out var number) || number < 0 || number > (int)StateFlags.All)
            throw new InvokeException("Invalid window state flags.");
        return (StateFlags)number;
    }

    private async Task SaveAsync(AppHandle app, StateFlags flags, CancellationToken token)
    {
        await _fileGate.WaitAsync(token);
        try
        {
            var path = await PathAsync(app, token);
            var states = ReadStates(path);
            foreach (var window in app.WebviewWindows)
            {
                if (!states.TryGetValue(window.Label, out var state)) state = new WindowSnapshot { Visible = true, Decorated = true };
                if (flags.HasFlag(StateFlags.Maximized)) state.Maximized = await window.IsMaximizedAsync(token);
                if (flags.HasFlag(StateFlags.Visible)) state.Visible = await window.IsVisibleAsync(token);
                if (flags.HasFlag(StateFlags.Decorations)) state.Decorated = await window.IsDecoratedAsync(token);
                if (flags.HasFlag(StateFlags.Fullscreen)) state.Fullscreen = await window.IsFullscreenAsync(token);
                var unstable = await window.IsMaximizedAsync(token) || await window.IsMinimizedAsync(token);
                if (!unstable && flags.HasFlag(StateFlags.Size))
                {
                    var size = await window.InnerSizeAsync(token);
                    if (size.Width > 0 && size.Height > 0) { state.Width = size.Width; state.Height = size.Height; }
                }
                if (!unstable && flags.HasFlag(StateFlags.Position))
                {
                    var position = await window.OuterPositionAsync(token);
                    state.X = position.X; state.Y = position.Y;
                }
                states[window.Label] = state;
            }
            await WriteStatesAsync(path, states, token);
        }
        finally { _fileGate.Release(); }
    }

    private static async Task WriteStatesAsync(string path, Dictionary<string, WindowSnapshot> states, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var content = Write(writer =>
        {
            writer.WriteStartObject();
            foreach (var pair in states)
            {
                writer.WritePropertyName(pair.Key); writer.WriteStartObject();
                writer.WriteNumber("width", pair.Value.Width); writer.WriteNumber("height", pair.Value.Height);
                writer.WriteNumber("x", pair.Value.X); writer.WriteNumber("y", pair.Value.Y);
                writer.WriteNumber("prev_x", pair.Value.X); writer.WriteNumber("prev_y", pair.Value.Y);
                writer.WriteBoolean("maximized", pair.Value.Maximized); writer.WriteBoolean("visible", pair.Value.Visible);
                writer.WriteBoolean("decorated", pair.Value.Decorated); writer.WriteBoolean("fullscreen", pair.Value.Fullscreen);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        });
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temporary, content.GetRawText(), token); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private async Task RestoreAsync(AppHandle app, string label, StateFlags flags, CancellationToken token)
    {
        var window = app.GetWindow(label) ?? throw new InvokeException($"Window '{label}' not found.");
        await _fileGate.WaitAsync(token);
        WindowSnapshot state;
        bool found;
        try { found = ReadStates(await PathAsync(app, token)).TryGetValue(label, out state); }
        finally { _fileGate.Release(); }
        if (!found) return;
        if (flags.HasFlag(StateFlags.Decorations)) await window.SetDecorationsAsync(state.Decorated, token);
        if (flags.HasFlag(StateFlags.Position) && await OnAvailableMonitor(window, state, token))
            await window.SetPositionAsync(new PhysicalPosition(state.X, state.Y), token);
        if (flags.HasFlag(StateFlags.Size) && state.Width > 0 && state.Height > 0)
            await window.SetSizeAsync(new PhysicalSize(state.Width, state.Height), token);
        if (flags.HasFlag(StateFlags.Maximized))
        {
            if (state.Maximized) await window.MaximizeAsync(token); else await window.UnmaximizeAsync(token);
        }
        if (flags.HasFlag(StateFlags.Fullscreen)) await window.SetFullscreenAsync(state.Fullscreen, token);
        if (flags.HasFlag(StateFlags.Visible))
        {
            if (state.Visible) await window.ShowAsync(token); else await window.HideAsync(token);
        }
    }

    private Task<string> PathAsync(AppHandle app, CancellationToken token) =>
        app.ResolveDirectoryAsync((int)BaseDirectory.AppConfig, _filename, token);

    private static async Task<bool> OnAvailableMonitor(Window window, WindowSnapshot state, CancellationToken token)
    {
        foreach (var monitor in await window.AvailableMonitorsAsync(token))
        {
            long left = monitor.Position.X, top = monitor.Position.Y;
            long right = left + monitor.Size.Width, bottom = top + monitor.Size.Height;
            long x = state.X, y = state.Y;
            if (x < right && x + state.Width >= left && y < bottom && y + state.Height >= top) return true;
        }
        return false;
    }

    private static JsonElement Write(Action<Utf8JsonWriter> action)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) action(writer);
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static Dictionary<string, WindowSnapshot> ReadStates(string path)
    {
        var states = new Dictionary<string, WindowSnapshot>(StringComparer.Ordinal);
        if (!File.Exists(path)) return states;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var pair in document.RootElement.EnumerateObject())
        {
            var value = pair.Value;
            states[pair.Name] = ReadSnapshot(value);
        }
        return states;
    }

    private static WindowSnapshot ReadSnapshot(JsonElement value) => new()
    {
        Width = value.GetProperty("width").GetUInt32(), Height = value.GetProperty("height").GetUInt32(),
        X = value.GetProperty("x").GetInt32(), Y = value.GetProperty("y").GetInt32(),
        Maximized = value.GetProperty("maximized").GetBoolean(), Visible = value.GetProperty("visible").GetBoolean(),
        Decorated = value.GetProperty("decorated").GetBoolean(), Fullscreen = value.GetProperty("fullscreen").GetBoolean()
    };

    private struct WindowSnapshot
    {
        public uint Width, Height;
        public int X, Y;
        public bool Maximized, Visible, Decorated, Fullscreen;
    }
}
