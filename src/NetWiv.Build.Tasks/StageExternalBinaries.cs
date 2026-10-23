using System.Text.Json.Nodes;
using Microsoft.Build.Framework;
using NetWiv.Build.Configuration;
using NetWiv.Build.Execution;
using NetWiv.Build.Packaging;

namespace NetWiv.Build.Tasks;

public sealed class StageExternalBinaries : Microsoft.Build.Utilities.Task
{
    [Required] public string ConfigPath { get; set; } = "";
    [Required] public string BaseDirectory { get; set; } = "";
    [Required] public string OutputDirectory { get; set; } = "";
    [Required] public string ApplicationName { get; set; } = "";
    [Required] public string Target { get; set; } = "";

    public override bool Execute()
    {
        try
        {
            var root = JsonNode.Parse(File.ReadAllBytes(ConfigPath)) as JsonObject ?? throw new NetWivConfigException("Configuration root must be an object.");
            var config = new ConfigDocument(ConfigPath, BaseDirectory, BaseDirectory, root, [ConfigPath]);
            var target = BuildTarget.Resolve(Target);
            var binary = ApplicationName + (target.Platform == "windows" ? ".exe" : "");
            ExternalBinaryStager.Stage(ExternalBinaryStager.Resolve(config, target, binary), OutputDirectory);
            return true;
        }
        catch (Exception error) when (error is NetWivConfigException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException or InvalidOperationException)
        {
            Log.LogError("NETWIVBUILD001: {0}", error.Message);
            return false;
        }
    }
}
