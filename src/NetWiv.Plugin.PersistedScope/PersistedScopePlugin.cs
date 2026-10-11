using System.Text.Json;
using NetWiv.Plugin.Dialog;
using NetWiv.Plugin.Fs;

namespace NetWiv.Plugin.PersistedScope;

/// <summary>Restores native-dialog selections across desktop app runs.</summary>
public sealed class PersistedScopePlugin : IPlugin
{
    private readonly object _gate = new();
    private readonly List<Selection> _selections = [];
    private DialogPlugin? _dialog;
    private string? _statePath;

    public string Name => "persisted-scope";
    public void Initialize(PluginContext context) { }

    public void Setup(AppHandle app)
    {
        var fs = app.TryPlugin<FsPlugin>() ?? throw new InvalidOperationException("Register FsPlugin before PersistedScopePlugin.");
        _dialog = app.TryPlugin<DialogPlugin>() ?? throw new InvalidOperationException("Register DialogPlugin before PersistedScopePlugin.");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var data = OperatingSystem.IsLinux()
            ? Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } xdg ? xdg : Path.Combine(home, ".local", "share")
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _statePath = Path.Combine(data, app.Metadata.Identifier, ".persisted-scope.netwiv.json");
        if (File.Exists(_statePath))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(_statePath));
                var restored = new List<Selection>();
                foreach (var item in document.RootElement.EnumerateArray())
                {
                    var selection = new Selection(item.GetProperty("path").GetString() ?? throw new JsonException("A persisted path is null."),
                        item.GetProperty("directory").GetBoolean(), item.GetProperty("recursive").GetBoolean());
                    if (!Path.IsPathFullyQualified(selection.Path)) continue;
                    restored.Add(selection);
                }
                foreach (var selection in restored)
                {
                    _selections.Add(selection);
                    fs.GrantSelectedPath(selection.Path, selection.Directory, selection.Recursive);
                    var assets = app.TryAssetProtocolScope();
                    if (selection.Directory) assets?.AllowDirectory(selection.Path, recursive: true);
                    else assets?.AllowFile(selection.Path);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
            {
                Console.Error.WriteLine($"Could not restore persisted scope: {error.Message}");
            }
        }
        _dialog.PathSelected += OnPathSelected;
    }

    private void OnPathSelected(string path, bool directory, bool recursive)
    {
        if (!Path.IsPathFullyQualified(path) || _statePath is null) return;
        lock (_gate)
        {
            var item = new Selection(path, directory, recursive);
            if (_selections.Contains(item)) return;
            _selections.Add(item);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
                var temporary = _statePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (var stream = File.Create(temporary))
                    using (var writer = new Utf8JsonWriter(stream))
                    {
                        writer.WriteStartArray();
                        foreach (var selection in _selections)
                        {
                            writer.WriteStartObject();
                            writer.WriteString("path", selection.Path);
                            writer.WriteBoolean("directory", selection.Directory);
                            writer.WriteBoolean("recursive", selection.Recursive);
                            writer.WriteEndObject();
                        }
                        writer.WriteEndArray();
                    }
                    File.Move(temporary, _statePath, true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Could not save persisted scope: {error.Message}");
            }
        }
    }

    public void Dispose()
    {
        if (_dialog is not null) _dialog.PathSelected -= OnPathSelected;
    }

    private sealed record Selection(string Path, bool Directory, bool Recursive);
}
