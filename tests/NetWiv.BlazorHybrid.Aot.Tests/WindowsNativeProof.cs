using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NetWiv.Platform.Desktop;
using NetWiv.Runtime;

namespace NetWiv.BlazorHybrid.Aot.Tests;

// Executes the product adapter, native backend, bundled script and real DOM.
// The browser reloads its actual document once before reporting final success.
internal static class WindowsNativeProof
{
    internal static int Run(bool managed = false)
    {
        ProofGate.AssertRuntime(managed);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The native acceptance test requires Windows.");

        var desktop = new DesktopRuntime();
        var services = new ServiceCollection().AddScoped<ProofScope>()
            .AddNetWivBlazorHybridAot(ProofGate.Configure).BuildServiceProvider();
        var options = new BlazorWebViewOptions();
        options.RootComponents.Add<CounterComponent>("#app", new Dictionary<string, object?> { ["Label"] = "NativeAOT counter" });
        var runtime = new BlazorDesktopRuntime(desktop, services, options);
        var passed = false;
        var reloaded = false;
        var documentIds = new HashSet<ulong>();
        Exception? failure = null;

        void Receive(RuntimeMessage message)
        {
            if (message.WebviewLabel != "main" || message.DocumentId == 0 ||
                message.Source.GetLeftPart(UriPartial.Authority) != options.AppBaseUri.GetLeftPart(UriPartial.Authority) ||
                !message.Json.StartsWith("__aot-proof:", StringComparison.Ordinal)) return;
            try
            {
                using var result = JsonDocument.Parse(message.Json.AsMemory("__aot-proof:".Length));
                if (!result.RootElement.GetProperty("passed").GetBoolean())
                    throw new InvalidOperationException("WebView assertions failed: " + result.RootElement.GetProperty("error").GetString());
                ProofGate.Check(CounterComponent.Count == 1 && CounterComponent.RenderedLabel == "NativeAOT counter",
                    "The browser did not update the product component.");
                var reply = CounterComponent.JsReply;
                ProofGate.Check(reply?.Name == "reply" && reply.Count == 7 && reply.Items?[0]?.Value == long.MinValue &&
                    reply.Items[1] is null && reply.Optional?.Value == 42, "The real JavaScript DTO changed.");
                if (result.RootElement.GetProperty("phase").GetInt32() == 0)
                {
                    ProofGate.Check(!reloaded, "The initial document ran twice.");
                    reloaded = true;
                    Console.WriteLine("PASS product NativeAOT renderer, bindings, module, DTO and callback disposal before reload");
                    desktop.EvaluateScript("main", "sessionStorage.setItem('netwiv-aot-phase','1'); location.reload();", message.DocumentId);
                }
                else
                {
                    ProofGate.Check(reloaded && documentIds.Count >= 2, "The browser did not create a new document.");
                    passed = true;
                    desktop.Exit();
                }
            }
            catch (Exception error)
            {
                failure = error;
                desktop.Exit(1);
            }
        }

        using var watchdog = new Timer(_ =>
        {
            Console.Error.WriteLine("FAIL Native WebView acceptance test timed out.");
            Environment.Exit(124);
        }, null, TimeSpan.FromSeconds(60), Timeout.InfiniteTimeSpan);
        try
        {
            runtime.Run(new RuntimeContext(
                [new("main", "NetWiv NativeAOT Hybrid acceptance", options.AppBaseUri.AbsoluteUri, 720, 480, default)],
                "", Receive, _ => { }, _ => new(404, "text/plain", "Not found"u8.ToArray()),
                OnDocumentChanged: change =>
                {
                    if (change.WebviewLabel == "main" && !change.Closed && change.DocumentId != 0)
                        documentIds.Add(change.DocumentId);
                }));
        }
        finally { services.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        if (failure is not null) throw new InvalidOperationException("Native WebView acceptance failed.", failure);
        ProofGate.Check(passed, "The native window closed before assertions completed.");
        CounterComponent.AssertScopesDisposed(2);
        Console.WriteLine("PASS genuine document reload, separate asynchronous scopes and process shutdown");
        return 0;
    }
}
