using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NetWiv;
using NetWiv.Plugin.Shell;
using NetWiv.Runtime;

internal static class Program
{
    private static async Task<int> Main()
    {
        Process? child = null;
        try
        {
            var runtime = new ConsumerRuntime();
            var echo = JsonValue.Parse("""{"name":"helpers/helper","sidecar":true,"args":["echo"]}""");
            var wait = JsonValue.Parse("""{"name":"helpers/helper","sidecar":true,"args":["wait"]}""");
            using var app = new Builder().UseRuntime(runtime).Plugin(new ShellPlugin())
                .Metadata(new AppMetadata("Sidecar consumer", "1.0.0", "dev.netwiv.sidecar-consumer"))
                .Window("main", "Sidecar consumer", "index.html")
                .Capability(new("main", ["shell:allow-execute", "shell:allow-spawn", "shell:allow-kill"], Windows: ["main"],
                    PermissionScopes: [new("shell:allow-execute", [echo], []), new("shell:allow-spawn", [wait], [])]))
                .Build();
            app.Run();
            var executed = await Invoke(runtime, "execute", """{"program":"helpers/helper","args":["echo"],"options":{"sidecar":true}}""");
            Check(executed.GetProperty("code").GetInt32() == 0 &&
                executed.GetProperty("stdout").GetString()!.Trim() == "sidecar-fixture-ok");
            await Invoke(runtime, "execute", """{"program":"helpers/helper","args":["denied"],"options":{"sidecar":true}}""", rejected: true);
            await Invoke(runtime, "execute", """{"program":"helpers/helper","args":["echo"],"options":{"sidecar":false}}""", rejected: true);
            var spawned = await Invoke(runtime, "spawn", """{"program":"helpers/helper","args":["wait"],"options":{"sidecar":true},"onEvent":"__CHANNEL__:91"}""");
            child = Process.GetProcessById(spawned.GetInt32());
            Check(!child.HasExited);
            await Invoke(runtime, "kill", "{\"pid\":" + child.Id + "}");
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Console.WriteLine("PASS default ShellPlugin staged sidecar execute scope spawn and kill");
            return 0;
        }
        catch
        {
            Console.Error.WriteLine("FAIL default ShellPlugin staged sidecar consumer");
            return 1;
        }
        finally
        {
            if (child is not null)
            {
                if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); }
                child.Dispose();
            }
        }
    }

    private static async Task<JsonElement> Invoke(ConsumerRuntime runtime, string command, string payload, bool rejected = false)
    {
        const string marker = "headers.set('Tauri-Invoke-Key', '";
        var context = runtime.Context!;
        var script = context.InitializationScript;
        var key = script.Substring(script.IndexOf(marker, StringComparison.Ordinal) + marker.Length, 64);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Tauri-Invoke-Key"] = key, ["Tauri-Callback"] = "1", ["Tauri-Error"] = "2",
            ["Origin"] = "tauri://localhost", ["Content-Type"] = "application/json"
        };
        var response = await context.InvokeProtocol!(new(new Uri("ipc://localhost/plugin%3Ashell%7C" + command),
            "POST", headers, Encoding.UTF8.GetBytes(payload), WebviewLabel: "main", WindowLabel: "main", DocumentId: 0));
        Check(response.Headers!["Tauri-Response"] == (rejected ? "error" : "ok"));
        return rejected ? JsonValue.Null : JsonValue.Parse(Encoding.UTF8.GetString(response.Body.Span));
    }

    private static void Check(bool value)
    {
        if (!value) throw new InvalidOperationException("Sidecar consumer assertion failed.");
    }

    private sealed class ConsumerRuntime : IDesktopRuntime
    {
        public RuntimeContext? Context { get; private set; }
        public void Run(RuntimeContext context) { Context = context; context.OnReady?.Invoke(); }
        public void Dispatch(Action action) => action();
        public Task<JsonElement> InvokeAsync(string command, JsonElement args, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The sidecar consumer does not forward native commands.");
        public void EvaluateScript(string webviewLabel, string script) { }
        public void Exit(int exitCode = 0) { }
    }
}
