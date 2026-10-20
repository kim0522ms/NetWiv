using NetWiv.Build.Frontend;
using Microsoft.Build.Framework;

namespace NetWiv.Build.Tasks;

public sealed class ExportFrontendBindings : Microsoft.Build.Utilities.Task
{
    [Required] public string AssemblyPath { get; set; } = "";
    /// <summary>Where commands.js and commands.d.ts go; empty to skip them.</summary>
    public string OutputDirectory { get; set; } = "";
    /// <summary>Where the typed C# client for a .NET frontend goes; empty to skip it.</summary>
    public string CSharpOutputDirectory { get; set; } = "";

    public override bool Execute()
    {
        try
        {
            var bindings = FrontendBindings.Read(AssemblyPath);
            if (OutputDirectory.Length != 0)
            {
                bindings.WriteTo(OutputDirectory);
                Log.LogMessage(MessageImportance.Low, "NetWiv frontend bindings: {0}", OutputDirectory);
            }
            if (CSharpOutputDirectory.Length != 0)
            {
                bindings.WriteCSharpTo(CSharpOutputDirectory);
                Log.LogMessage(MessageImportance.Low, "NetWiv C# client: {0}", CSharpOutputDirectory);
            }
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or BadImageFormatException or ArgumentException)
        {
            Log.LogError("DTOXBUILD001: {0}", exception.Message);
            return false;
        }
    }
}
