using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using NetWiv;
using NetWiv.Hosting;
using NetWiv.Platform.Desktop;

[assembly: CommandJsonContext(typeof(AppJsonContext))]
[assembly: GenerateFrontendBindings]

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var smoke = args.Contains("--smoke-test", StringComparer.Ordinal);
        var builder = NetWivApplication.CreateBuilder(args.Where(arg => arg != "--smoke-test").ToArray());
        var audit = new InvocationAudit();
        builder.Services.Configure<GreetingOptions>(builder.Configuration.GetSection("Greeting"));
        builder.Services.AddSingleton(audit);
        builder.Services.AddScoped<InvocationWorkspace>();
        builder.Services.AddScoped<GreetingCommands>();
        builder.Services.AddScoped<EquipmentPlugin>();

        builder.Desktop
            .UseRuntime(new DesktopRuntime())
            .Metadata(new("NetWiv Hosting sample", "0.1.0", "dev.netwiv.hostingsample"))
            .UseEmbeddedAssets()
            .UseEmbeddedConfig()
            .Window("main", "NetWiv Hosting sample", smoke ? "index.html?smoke=1" : "index.html", 800, 600)
            .InvokeHandler("greet")
            .AppCommandManifest("greet")
            .Plugin(EquipmentPlugin.CreateLifecycle(audit))
            .Capability(new("main", ["allow-greet", "equipment:allow-status", "equipment:allow-unselected", "equipment:allow-reset", "equipment:deny-reset"], Windows: ["main"]));
        if (smoke)
        {
            builder.Desktop.InvokeHandler("audit_snapshot", "smoke_done")
                .AppCommandManifest("audit_snapshot", "smoke_done")
                .Capability(new("smoke", ["allow-audit-snapshot", "allow-smoke-done"], Windows: ["main"]));
        }

        // Run stays on the original main thread. The watchdog is only for automated smoke runs.
        using var watchdog = smoke ? new Timer(_ => Environment.Exit(124), null, TimeSpan.FromSeconds(60), Timeout.InfiniteTimeSpan) : null;
        using (var app = builder.Build()) app.Run();
        if (!smoke) return 0;
        if (!audit.SmokePassed || audit.LifecycleInitialized != 1 || audit.LifecycleDisposed != 1)
        {
            Console.Error.WriteLine("FAIL: Hosting sample did not finish or dispose its lifecycle plugin.");
            return 1;
        }
        Console.WriteLine("PASS: Hosting sample frontend IPC, scoped disposal, ACL and lifecycle cleanup");
        return 0;
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GreetingResult))]
[JsonSerializable(typeof(EquipmentStatus))]
[JsonSerializable(typeof(AuditSnapshot))]
[JsonSerializable(typeof(string))]
public partial class AppJsonContext : JsonSerializerContext;
