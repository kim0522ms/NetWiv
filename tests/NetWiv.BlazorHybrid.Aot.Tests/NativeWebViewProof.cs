using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NetWiv.Platform.Desktop;
using NetWiv.Runtime;

namespace NetWiv.BlazorHybrid.Aot.Tests;

// Executes the product adapter, native backend, bundled script and real DOM.
// The browser reloads its actual document once before reporting final success.
internal static class NativeWebViewProof
{
    internal static int Run(bool managed = false, bool managedHost = false, bool backForward = false)
    {
        if (managedHost) ProofGate.AssertManagedHost();
        else ProofGate.AssertRuntime(managed);
        if (managedHost && !System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
            throw new PlatformNotSupportedException("The Microsoft managed host requires managed execution.");

        var desktop = new DesktopRuntime();
        var registrations = new ServiceCollection().AddScoped<ProofScope>();
        if (System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported && managedHost) registrations.AddNetWivBlazorHybrid();
        else registrations.AddNetWivBlazorHybridAot(ProofGate.Configure);
        var services = registrations.BuildServiceProvider();
        var options = new BlazorWebViewOptions();
        if (System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported && managedHost) options.RootComponents.Add<ManagedCounterComponent>("#app", new Dictionary<string, object?> { ["Label"] = "Managed counter" });
        else options.RootComponents.Add<CounterComponent>("#app", new Dictionary<string, object?> { ["Label"] = "NativeAOT counter", ["NativeInteropChecks"] = true });
        var passed = false;
        var reloaded = false;
        var returned = false;
        var documentIds = new HashSet<ulong>();
        Exception? failure = null;
        var protocolCalls = 0;
        var commandCalls = 0;
        var increments = 0;
        ulong provisionalDocument = 0;
        using var failedNavigation = OperatingSystem.IsLinux() ? new ProvisionalNavigationProbe() : null;

        void Receive(RuntimeMessage message)
        {
            if (message.Json.StartsWith("__transport-forged:", StringComparison.Ordinal))
            {
                if (OperatingSystem.IsLinux() || message.Source.GetLeftPart(UriPartial.Authority) == options.AppBaseUri.GetLeftPart(UriPartial.Authority))
                {
                    failure = new InvalidOperationException("A foreign frame was attributed to the application document.");
                    desktop.Exit(1);
                }
                return;
            }
            if (message.WebviewLabel != "main" || message.DocumentId == 0 ||
                message.Source.GetLeftPart(UriPartial.Authority) != options.AppBaseUri.GetLeftPart(UriPartial.Authority) ||
                !message.Json.StartsWith("__aot-proof:", StringComparison.Ordinal)) return;
            try
            {
                using var result = JsonDocument.Parse(message.Json.AsMemory("__aot-proof:".Length));
                if (!result.RootElement.GetProperty("passed").GetBoolean())
                    throw new InvalidOperationException("WebView assertions failed: " + result.RootElement.GetProperty("error").GetString());
                ProofGate.Check(System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported && managedHost
                    ? ManagedCounterComponent.Count == 1 && ManagedCounterComponent.RenderedLabel == "Managed counter"
                    : CounterComponent.Count == 1 && CounterComponent.RenderedLabel == "NativeAOT counter",
                    "The browser did not update the product component.");
                var reply = System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported && managedHost ? ManagedCounterComponent.JsReply : CounterComponent.JsReply;
                ProofGate.Check(reply?.Name == "reply" && reply.Count == 7 && reply.Items?[0]?.Value == long.MinValue &&
                    reply.Items[1] is null && reply.Optional?.Value == 42, "The real JavaScript DTO changed.");
                if (!reloaded)
                {
                    ProofGate.Check(!reloaded, "The initial document ran twice.");
                    reloaded = true;
                    Console.WriteLine(managedHost
                        ? "PASS Microsoft managed renderer, bindings, module, DTO and callback disposal before reload"
                        : "PASS product NativeAOT renderer, bindings, module, DTO and callback disposal before reload");
                    desktop.EvaluateScript("main", "location.href='?netwiv-proof-phase=1';", message.DocumentId);
                }
                else if (backForward && !returned)
                {
                    returned = true;
                    desktop.EvaluateScript("main", "window.name='netwiv-hybrid-back'; history.back();", message.DocumentId);
                }
                else
                {
                    ProofGate.Check(reloaded && documentIds.Count >= 2, "The browser did not create a new document.");
                    if (backForward)
                    {
                        var navigationType = result.RootElement.GetProperty("navigationType").GetString();
                        var restoredAndReloaded = documentIds.Count >= 4 && navigationType == "reload";
                        var freshWindowsReturn = OperatingSystem.IsWindows() &&
                            documentIds.Count >= 3 && navigationType == "back_forward";
                        ProofGate.Check(restoredAndReloaded || freshWindowsReturn,
                            $"The Hybrid return did not create a fresh document (documents={documentIds.Count}, navigation={navigationType}).");
                    }
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
            var observed = new ObservedRuntime(desktop, Receive, change =>
                {
                    if (change.WebviewLabel == "main" && !change.Closed && change.DocumentId != 0)
                        documentIds.Add(change.DocumentId);
                }, request =>
                {
                    ProofGate.Check(!request.Headers.ContainsKey("netwiv-document-capability") &&
                        !request.Headers.ContainsKey("netwiv-document-id"), "Transport credentials escaped into managed request headers.");
                    if (request.Method == "POST" && request.Uri.AbsolutePath == "/transport_echo") Interlocked.Increment(ref protocolCalls);
                });
            using var app = new Builder().UseServiceProvider(services).UseBlazor(observed, options)
                .Window("main", "NetWiv Hybrid acceptance", options.AppBaseUri.AbsoluteUri, 720, 480)
                .RegisterCommand("failure_url", (_, _, _) => ValueTask.FromResult(JsonValue.From(failedNavigation!.Url)))
                .RegisterCommand("transport_increment", (_, _, _) => ValueTask.FromResult(JsonValue.From(Interlocked.Increment(ref increments))))
                .RegisterCommand("transport_count", (_, _, _) => ValueTask.FromResult(JsonValue.From(Volatile.Read(ref increments))))
                .RegisterCommand("transport_echo", async (context, args, _) =>
                {
                    var input = args.GetProperty("input").GetString();
                    if (input == "pending")
                    {
                        await failedNavigation!.Requested.Task;
                        provisionalDocument = context.DocumentId;
                    }
                    if (input is "after-cancel" or "after-message")
                        ProofGate.Check(context.DocumentId == provisionalDocument && documentIds.Count == 1,
                            "A canceled provisional navigation retired the committed document.");
                    ProofGate.Check(context.Source.GetLeftPart(UriPartial.Authority) == options.AppBaseUri.GetLeftPart(UriPartial.Authority),
                        "A provisional navigation changed the authenticated document source.");
                    ProofGate.Check(!context.Request.Headers().ContainsKey("netwiv-document-capability") &&
                        !context.Request.Headers().ContainsKey("netwiv-document-id"), "Transport credentials escaped into command headers.");
                    Interlocked.Increment(ref commandCalls);
                    return args.GetProperty("input").Clone();
                }).Build();
            app.Run();
        }
        finally { services.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        if (failure is not null) throw new InvalidOperationException("Native WebView acceptance failed: " + failure.Message, failure);
        ProofGate.Check(passed, "The native window closed before assertions completed.");
        var expectedProtocolCalls = (backForward ? 3 : 2) + (OperatingSystem.IsLinux() ? 1 : 0);
        var expectedCommandCalls = (backForward ? 6 : 4) + (OperatingSystem.IsLinux() ? 3 : 0);
        ProofGate.Check(protocolCalls == expectedProtocolCalls && commandCalls == expectedCommandCalls, $"Native protocol and message command paths did not both execute on both documents (protocol={protocolCalls}, commands={commandCalls}).");
        ProofGate.Check(increments == (backForward ? 3 : 2), "A failed protocol response replayed a native command.");
        if (System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported && managedHost) ManagedCounterComponent.AssertScopesDisposed(backForward ? 3 : 2);
        else CounterComponent.AssertScopesDisposed(backForward ? 3 : 2);
        Console.WriteLine("PASS genuine document reload, separate asynchronous scopes and process shutdown");
        Console.WriteLine("PASS foreign frame rejection, native protocol and message commands without credential disclosure");
        Console.WriteLine("PASS protocol response failure rejects without replay, then subsequent calls use message IPC");
        if (OperatingSystem.IsLinux()) Console.WriteLine("PASS retained document commands and rendering across canceled provisional navigation");
        if (backForward) Console.WriteLine("PASS Hybrid history return creates a fresh native document and component scope");
        return 0;
    }

    private sealed class ObservedRuntime(IDesktopRuntime inner, Action<RuntimeMessage> receive,
        Action<RuntimeDocumentChange> changed, Action<AssetRequest> protocol) : IDesktopRuntime
    {
        public void Run(RuntimeContext context) => inner.Run(context with
        {
            OnMessage = message =>
            {
                if (message.Json.StartsWith("__aot-proof:", StringComparison.Ordinal) ||
                    message.Json.StartsWith("__transport-forged:", StringComparison.Ordinal)) receive(message);
                else context.OnMessage(message);
            },
            OnDocumentChanged = change => { changed(change); context.OnDocumentChanged?.Invoke(change); },
            InvokeProtocol = async request =>
            {
                protocol(request);
                var response = await context.InvokeProtocol!(request);
                // Corrupt the response only after the real command has executed.
                return request.Method == "POST" && request.Uri.AbsolutePath == "/transport_increment"
                    ? response with { ContentType = "application/json", Body = "invalid JSON"u8.ToArray() }
                    : response;
            },
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
