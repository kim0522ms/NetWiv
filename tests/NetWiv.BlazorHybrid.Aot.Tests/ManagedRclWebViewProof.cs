using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NetWiv.BlazorHybrid;
using NetWiv.BlazorHybrid.Managed.Fixture;
using NetWiv.Platform.Desktop;
using NetWiv.Runtime;

namespace NetWiv.BlazorHybrid.Aot.Tests;

internal static class ManagedRclWebViewProof
{
    internal static int Run()
    {
        ProofGate.AssertManagedHost();
        var desktop = new DesktopRuntime();
        var services = new ServiceCollection()
            .AddScoped<ManagedFixtureScope>()
            .AddNetWivBlazorHybrid()
            .BuildServiceProvider();
        var options = new BlazorWebViewOptions { HostPage = "wwwroot/managed-rcl-index.html" };
        options.RootComponents.Add<FixtureRoot>("#app");
        var documents = new HashSet<ulong>();
        var passes = 0;
        Exception? failure = null;

        void Receive(RuntimeMessage message)
        {
            if (message.WebviewLabel != "main" || message.DocumentId == 0 ||
                message.Source.GetLeftPart(UriPartial.Authority) != options.AppBaseUri.GetLeftPart(UriPartial.Authority) ||
                !message.Json.StartsWith("__managed-rcl-proof:", StringComparison.Ordinal)) return;
            try
            {
                using var result = JsonDocument.Parse(message.Json.AsMemory("__managed-rcl-proof:".Length));
                if (!result.RootElement.GetProperty("passed").GetBoolean())
                    throw new InvalidOperationException("RCL browser assertions failed: " + result.RootElement.GetProperty("error").GetString());
                if (++passes == 1)
                    desktop.EvaluateScript("main", "location.reload();", message.DocumentId);
                else
                    desktop.Exit();
            }
            catch (Exception error)
            {
                failure = error;
                desktop.Exit(1);
            }
        }

        using var watchdog = new Timer(_ =>
        {
            Console.Error.WriteLine("FAIL Managed RCL WebView proof timed out.");
            Environment.Exit(124);
        }, null, TimeSpan.FromSeconds(60), Timeout.InfiniteTimeSpan);
        try
        {
            var observed = new ObservedRuntime(desktop, Receive, change =>
            {
                if (change.WebviewLabel == "main" && !change.Closed && change.DocumentId != 0)
                    documents.Add(change.DocumentId);
            });
            using var app = new Builder().UseServiceProvider(services).UseBlazor(observed, options)
                .Window("main", "NetWiv managed RCL proof", options.AppBaseUri.AbsoluteUri, 720, 480)
                .Build();
            app.Run();
        }
        finally { services.DisposeAsync().AsTask().GetAwaiter().GetResult(); }

        if (failure is not null) throw new InvalidOperationException("Managed RCL WebView proof failed: " + failure.Message, failure);
        ProofGate.Check(passes == 2 && documents.Count >= 2, "The managed RCL browser did not complete an initial and reloaded document.");
        ManagedFixtureProofState.AssertCleaned(2);
        Console.WriteLine("PASS managed RCL route discovery, EditForm validation and RCL JavaScript module");
        Console.WriteLine("PASS asynchronous component, module and page-scope cleanup after reload and shutdown");
        return 0;
    }

    private sealed class ObservedRuntime(IDesktopRuntime inner, Action<RuntimeMessage> receive,
        Action<RuntimeDocumentChange> changed) : IDesktopRuntime
    {
        public void Run(RuntimeContext context) => inner.Run(context with
        {
            OnMessage = message =>
            {
                if (message.Json.StartsWith("__managed-rcl-proof:", StringComparison.Ordinal)) receive(message);
                else context.OnMessage(message);
            },
            OnDocumentChanged = change => { changed(change); context.OnDocumentChanged?.Invoke(change); },
        });
        public void Dispatch(Action action) => inner.Dispatch(action);
        public Task<JsonElement> InvokeAsync(string command, JsonElement args, CancellationToken cancellationToken = default) => inner.InvokeAsync(command, args, cancellationToken);
        public Task<JsonElement> InvokeAsync(string command, JsonElement args, RuntimeCommandContext context, CancellationToken cancellationToken = default) => inner.InvokeAsync(command, args, context, cancellationToken);
        public Task<RuntimeInvokeResponse> InvokeResponseAsync(string command, JsonElement args, RuntimeCommandContext context, CancellationToken cancellationToken = default) => inner.InvokeResponseAsync(command, args, context, cancellationToken);
        public void EvaluateScript(string label, string script) => inner.EvaluateScript(label, script);
        public void EvaluateScript(string label, string script, ulong documentId) => inner.EvaluateScript(label, script, documentId);
        public void Exit(int code = 0) => inner.Exit(code);
    }
}
