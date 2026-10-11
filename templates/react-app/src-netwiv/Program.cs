using System.Text.Json.Serialization;
using NetWiv;
using NetWiv.Platform.Desktop;

[assembly: CommandJsonContext(typeof(AppJsonContext))]
[assembly: GenerateFrontendBindings]

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var dev = args.Contains("--dev", StringComparer.Ordinal);
        var smoke = args.Contains("--smoke-test", StringComparer.Ordinal);
//-:cnd:noEmit
#if !DEBUG
        if (dev) throw new InvalidOperationException("The Vite dev server is only enabled in Debug builds.");
#endif
//+:cnd:noEmit
        var url = dev ? "http://127.0.0.1:5173/" : "index.html";
        if (smoke) url += "?smoke=1";
        var builder = new Builder()
            .UseRuntime(new DesktopRuntime())
            .Metadata(new AppMetadata("NetWiv React example", "0.1.0", "dev.netwiv.reactexample"))
            .UseEmbeddedAssets()
            .Manage(new GreetingCounter())
            .InvokeHandler("greet")
            .Window("main", "NetWiv + React", url, 900, 680)
            .UseEmbeddedConfig(dev ? EmbeddedConfigMode.Development : EmbeddedConfigMode.Production);
        if (dev) builder.DevelopmentOrigin("http://127.0.0.1:5173");
        if (smoke) builder.InvokeHandler("smoke_done");
        using var app = builder.Build();
        app.Run();
    }
}

public sealed class GreetingCounter
{
    private int _count;
    public int Next() => Interlocked.Increment(ref _count);
}

public sealed record Greeting(string Message, int Count);

public static class Commands
{
    [Command]
    public static Greeting Greet(string name, State<GreetingCounter> counter)
        => new($"Hello, {name}!", counter.Value.Next());

    [Command]
    public static void SmokeDone(string result, AppHandle app)
    {
        Console.WriteLine(result);
        app.Dispatch(() => app.Exit(result == "PASS: React form → generated C# → JSON" ? 0 : 1));
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(Greeting))]
public partial class AppJsonContext : JsonSerializerContext;
