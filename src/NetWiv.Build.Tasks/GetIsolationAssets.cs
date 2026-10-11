using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace NetWiv.Build.Tasks;

public sealed class GetIsolationAssets : Microsoft.Build.Utilities.Task
{
    public string ConfigPath { get; set; } = "";
    public string IsolationDirectory { get; set; } = "";
    [Output] public ITaskItem[] Assets { get; set; } = [];

    public override bool Execute()
    {
        try
        {
            var directory = IsolationDirectory;
            if (directory.Length == 0 && ConfigPath.Length > 0 && File.Exists(ConfigPath))
            {
                using var document = JsonDocument.Parse(File.ReadAllBytes(ConfigPath));
                var root = document.RootElement;
                if (!root.TryGetProperty("app", out var app) || !app.TryGetProperty("security", out var security) ||
                    !security.TryGetProperty("pattern", out var pattern) || pattern.GetProperty("use").GetString() != "isolation") return true;
                directory = Path.GetFullPath(pattern.GetProperty("options").GetProperty("dir").GetString()!, Path.GetDirectoryName(Path.GetFullPath(ConfigPath))!);
            }
            if (directory.Length == 0) return true;
            directory = Path.GetFullPath(directory);
            if (!File.Exists(Path.Combine(directory, "index.html"))) throw new InvalidOperationException($"Isolation directory must contain index.html: {directory}");
            var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
            if (!files.Any(static path => File.ReadAllText(path).Contains("__TAURI_ISOLATION_HOOK__", StringComparison.Ordinal)))
                throw new InvalidOperationException("The isolation application must set window.__TAURI_ISOLATION_HOOK__ to a function or null.");
            Assets = files.Select(path =>
            {
                var item = new TaskItem(path);
                item.SetMetadata("LogicalName", "netwiv.isolation." + Path.GetRelativePath(directory, path).Replace('\\', '/'));
                return (ITaskItem)item;
            }).ToArray();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException or KeyNotFoundException)
        {
            Log.LogError("DTOXBUILD002: {0}", exception.Message);
            return false;
        }
    }
}
