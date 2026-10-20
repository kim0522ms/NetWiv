using System.Text;
using NetWiv.Platform.Desktop;
using NetWiv.Runtime;

namespace NetWiv.BlazorHybrid.Aot.Tests;

// Ordinary Core IPC must also work when WebKit restores a cached JS document.
internal static class TransportNavigationProof
{
    internal static int Run()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("This transport lifecycle test requires WebKitGTK.");
        var visits = new List<(string Page, ulong Epoch)>();
        var passed = false;
        var runtime = new DesktopRuntime();
        using var watchdog = new Timer(_ => Environment.Exit(124), null, TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);
        using var app = new Builder().UseRuntime(runtime).Window("main", "NetWiv transport navigation", "a.html")
            .ResolveAssets(_ => new AssetResponse(200, "text/html", Encoding.UTF8.GetBytes(Page)))
            .RegisterCommand("visit", (context, args, _) =>
            {
                var page = args.GetProperty("page").GetString()!;
                ProofGate.Check(context.Source.AbsolutePath == page && context.DocumentId != 0, "Navigation changed the authenticated source or lost its epoch.");
                visits.Add((page, context.DocumentId));
                var action = visits.Count switch { 1 when page == "/a.html" => "forward", 2 when page == "/b.html" => "back", 3 when page == "/a.html" => "done", _ => throw new InvalidOperationException("Unexpected navigation sequence.") };
                if (action == "done")
                {
                    ProofGate.Check(args.GetProperty("restored").GetBoolean(), "WebKit did not restore the cached JavaScript document.");
                    ProofGate.Check(visits.Select(visit => visit.Epoch).Distinct().Count() == 3, "Back navigation reused a retired epoch.");
                    passed = true;
                    runtime.Exit();
                }
                return ValueTask.FromResult(JsonValue.From(action));
            }).Build();
        app.Run();
        ProofGate.Check(passed, "Back/forward transport assertions did not complete.");
        Console.WriteLine("PASS cached JavaScript document restore queues fresh Core IPC and receives a new native epoch");
        return 0;
    }

    private const string Page = """
        <!doctype html><script>
        async function check(restored) {
          const action = await window.__TAURI_INTERNALS__.invokeJson('visit', JSON.stringify({ page: location.pathname, restored }));
          if (JSON.parse(action) === 'forward') location.href = 'b.html';
          else if (JSON.parse(action) === 'back') history.back();
        }
        addEventListener('pageshow', event => { if (event.persisted) check(true); });
        check(false);
        </script>
        """;
}
