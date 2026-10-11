using System.Text.Json;
using NetWiv.Build.Packaging;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace NetWiv.Build.Tasks;

public sealed class GetBundleResources : Microsoft.Build.Utilities.Task
{
    [Required] public string ConfigPath { get; set; } = "";
    public string BaseDirectory { get; set; } = "";
    [Output] public string ResolvedBaseDirectory { get; set; } = "";
    [Output] public ITaskItem[] Resources { get; set; } = [];

    public override bool Execute()
    {
        try
        {
            ResolvedBaseDirectory = BaseDirectory.Length > 0 ? Path.GetFullPath(BaseDirectory) : Path.GetDirectoryName(Path.GetFullPath(ConfigPath))!;
            using var document = JsonDocument.Parse(File.ReadAllBytes(ConfigPath));
            if (!document.RootElement.TryGetProperty("bundle", out var bundle) || !bundle.TryGetProperty("resources", out var resources)) return true;
            var expansion = ResourcePaths.Expand(resources, ResolvedBaseDirectory);
            foreach (var error in expansion.Errors) Log.LogError("DTOXBUILD004: {0}", error.Message);
            if (expansion.Errors.Count > 0) return false;
            Resources = expansion.Files.Select(resource =>
            {
                var item = new TaskItem(Path.GetFullPath(resource.SourcePath, ResolvedBaseDirectory));
                item.SetMetadata("TargetPath", resource.TargetPath);
                return (ITaskItem)item;
            }).ToArray();
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        {
            Log.LogError("DTOXBUILD004: {0}", error.Message);
            return false;
        }
    }
}
