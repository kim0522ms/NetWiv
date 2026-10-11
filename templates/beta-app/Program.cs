using System.Text.Json.Serialization;
using NetWiv;
using NetWiv.Platform.Wry;

[assembly: CommandJsonContext(typeof(AppJsonContext))]

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var smoke = args.Contains("--smoke-test", StringComparer.Ordinal);
        var builder = new Builder()
            .UseRuntime(new WryDesktopRuntime())
            .Metadata(new AppMetadata("My NetWiv app", "0.1.0", "dev.netwiv.starter"))
            .UseEmbeddedAssets()
            .Manage(new GreetingCounter())
            .InvokeHandler("greet")
            .Window("main", "My NetWiv app", smoke ? "index.html?smoke=1" : "index.html", 800, 560);
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

    // Registered only when the maintainer's automated smoke test is requested.
    [Command]
    public static void SmokeDone(string result, AppHandle app)
    {
        Console.WriteLine(result);
        app.Dispatch(() => app.Exit(result == "PASS: official JS → generated C# → JSON" ? 0 : 1));
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(Greeting))]
public partial class AppJsonContext : JsonSerializerContext;
