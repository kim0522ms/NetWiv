namespace NetWiv.BlazorHybrid.Aot.Tests;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            ProofGate.AssertRuntime(args.Contains("--managed", StringComparer.Ordinal));
            return args.Contains("--native-webview", StringComparer.Ordinal)
                ? WindowsNativeProof.Run(args.Contains("--managed", StringComparer.Ordinal))
                : ProtocolProof.RunAsync().GetAwaiter().GetResult();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"FAIL {error.GetType().Name}: {error.Message}");
            return 1;
        }
    }
}
