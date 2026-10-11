using NetWiv.Build.Frontend;
using Microsoft.Build.Framework;

namespace NetWiv.Build.Tasks;

public sealed class ExportFrontendBindings : Microsoft.Build.Utilities.Task
{
    [Required] public string AssemblyPath { get; set; } = "";
    [Required] public string OutputDirectory { get; set; } = "";

    public override bool Execute()
    {
        try
        {
            FrontendBindings.Read(AssemblyPath).WriteTo(OutputDirectory);
            Log.LogMessage(MessageImportance.Low, "NetWiv frontend bindings: {0}", OutputDirectory);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or BadImageFormatException or ArgumentException)
        {
            Log.LogError("DTOXBUILD001: {0}", exception.Message);
            return false;
        }
    }
}
