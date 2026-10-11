using System.Text.Json.Serialization;
using NetWiv;
using NetWiv.Platform.Wry;
using NetWiv.Runtime;

[assembly: CommandJsonContext(typeof(SampleJsonContext))]

internal static class Program
{
    [STAThread]
    public static void Main()
    {
        var assetRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        new Builder()
            .UseRuntime(new WryDesktopRuntime())
            .Metadata(new AppMetadata("NetWiv sample", "1.0.0", "com.netwiv.sample"))
            .Manage(new GreetingCounter())
            .InvokeHandler("greet")
            .ResolveAssets(request => SampleAssets.Resolve(assetRoot, request))
            .Window("main", "NetWiv NativeAOT sample", "tauri://localhost/index.html", 720, 480)
            .Run();
    }
}

internal static class SampleAssets
{
    public static AssetResponse? Resolve(string root, AssetRequest request)
    {
        if (!string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var rootPath = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var relativePath = Uri.UnescapeDataString(request.Uri.AbsolutePath).TrimStart('/')
            .Replace('/', Path.DirectorySeparatorChar);
        var candidate = Path.GetFullPath(Path.Combine(rootPath, relativePath));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!candidate.StartsWith(rootPath, comparison) || !File.Exists(candidate))
        {
            return null;
        }

        var contentType = Path.GetExtension(candidate).ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            _ => "application/octet-stream",
        };
        return new AssetResponse(200, contentType, File.ReadAllBytes(candidate));
    }
}

public sealed class GreetingCounter
{
    private int _value;
    public int Next() => Interlocked.Increment(ref _value);
}

public sealed record Greeting(string Message, int Count);

public static class Commands
{
    [Command]
    public static Task<Greeting> Greet(
        string name,
        State<GreetingCounter> counter,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = new Greeting($"Hello, {name}!", counter.Value.Next());
        return Task.FromResult(value);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(Greeting))]
public partial class SampleJsonContext : JsonSerializerContext;
