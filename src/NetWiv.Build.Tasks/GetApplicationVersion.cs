using System.Text.Json;
using NetWiv.Build.Configuration;
using Microsoft.Build.Framework;

namespace NetWiv.Build.Tasks;

public sealed class GetApplicationVersion : Microsoft.Build.Utilities.Task
{
    [Required] public string ConfigPath { get; set; } = "";
    [Output] public string FileVersion { get; set; } = "";
    [Output] public string InformationalVersion { get; set; } = "";
    [Output] public string InputFile { get; set; } = "";

    public override bool Execute()
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(ConfigPath));
            if (!document.RootElement.TryGetProperty("version", out var version) || version.ValueKind == JsonValueKind.Null) return true;
            InformationalVersion = ApplicationVersion.Resolve(version.GetString()!, Path.GetDirectoryName(Path.GetFullPath(ConfigPath))!, out var input);
            InputFile = input ?? "";
            FileVersion = WindowsFileVersion.Format(InformationalVersion);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or JsonException)
        {
            Log.LogError("DTOXBUILD003: {0}", exception.Message);
            return false;
        }
    }
}
