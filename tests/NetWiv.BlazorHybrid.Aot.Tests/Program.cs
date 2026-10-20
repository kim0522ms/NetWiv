namespace NetWiv.BlazorHybrid.Aot.Tests;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Contains("--transport-navigation", StringComparer.Ordinal)) return TransportNavigationProof.Run();
            var managedHost = args.Contains("--managed-host", StringComparer.Ordinal);
            var managed = managedHost || args.Contains("--managed", StringComparer.Ordinal);
            if (managedHost && !args.Contains("--native-webview", StringComparer.Ordinal))
                throw new ArgumentException("--managed-host requires --native-webview.");
            if (managedHost) ProofGate.AssertManagedHost();
            else ProofGate.AssertRuntime(managed);
            return args.Contains("--native-webview", StringComparer.Ordinal)
                ? NativeWebViewProof.Run(managed, managedHost, args.Contains("--back-forward", StringComparer.Ordinal))
                : ProtocolProof.RunAsync().GetAwaiter().GetResult();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"FAIL {error.GetType().Name}: {error.Message}");
            return 1;
        }
    }
}
