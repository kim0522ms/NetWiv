using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace NetWiv.Build.Tasks;

public sealed class GetApplicationIcon : Microsoft.Build.Utilities.Task
{
    [Required] public string ConfigPath { get; set; } = "";
    public string BaseDirectory { get; set; } = "";
    [Output] public ITaskItem[] RuntimeIcon { get; set; } = [];
    [Output] public string WindowsIcon { get; set; } = "";

    public override bool Execute()
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(ConfigPath));
            if (!document.RootElement.TryGetProperty("bundle", out var bundle) ||
                !bundle.TryGetProperty("icon", out var icons) || icons.ValueKind == JsonValueKind.Null) return true;
            var paths = icons.ValueKind == JsonValueKind.String
                ? new[] { icons.GetString()! }
                : icons.EnumerateArray().Select(icon => icon.GetString()!).ToArray();
            var directory = BaseDirectory.Length > 0 ? Path.GetFullPath(BaseDirectory) : Path.GetDirectoryName(Path.GetFullPath(ConfigPath))!;
            var runtime = paths.FirstOrDefault(path => string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
                ?? paths.FirstOrDefault(path => string.Equals(Path.GetExtension(path), ".ico", StringComparison.OrdinalIgnoreCase));
            var windows = paths.FirstOrDefault(path => string.Equals(Path.GetExtension(path), ".ico", StringComparison.OrdinalIgnoreCase));
            if (runtime is not null)
            {
                var item = new TaskItem(Path.GetFullPath(runtime, directory));
                if (!File.Exists(item.ItemSpec)) throw new FileNotFoundException("Configured application icon is missing.", runtime);
                item.SetMetadata("LogicalName", "netwiv.default-icon");
                RuntimeIcon = [item];
            }
            if (windows is not null) WindowsIcon = Path.GetFullPath(windows, directory);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or JsonException)
        {
            Log.LogError("NETWIVBUILD005: {0}", exception.Message);
            return false;
        }
    }
}
