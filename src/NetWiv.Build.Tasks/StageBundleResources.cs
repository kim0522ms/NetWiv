using NetWiv.Build.Packaging;
using Microsoft.Build.Framework;

namespace NetWiv.Build.Tasks;

public sealed class StageBundleResources : Microsoft.Build.Utilities.Task
{
    [Required] public string BaseDirectory { get; set; } = "";
    [Required] public string OutputDirectory { get; set; } = "";
    public ITaskItem[] Resources { get; set; } = [];

    public override bool Execute()
    {
        try
        {
            ResourceStager.Stage(Resources.Select(item => new BundleResource(item.ItemSpec, item.GetMetadata("TargetPath"))).ToArray(), BaseDirectory, OutputDirectory);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        {
            Log.LogError("DTOXBUILD005: {0}", error.Message);
            return false;
        }
    }
}
