using System.Text;
using System.Text.Json;
using NetWiv.Platform.Desktop;
using NetWiv.Runtime;

namespace NetWiv.BlazorHybrid.Aot.Tests;

// A public custom protocol named ipc owns its assets; it is not the built-in
// command transport, including when its nullable resolver declines a request.
internal static class RegisteredIpcProof
{
    internal static int Run()
    {
        var desktop = new DesktopRuntime();
        var builtinCalls = 0;
        var resourceGets = 0;
        var resourcePosts = 0;
        var commandCalls = 0;
        var passed = false;
        string? failure = null;
        var runtime = new ProbeRuntime(desktop, () => Interlocked.Increment(ref builtinCalls));
        using var watchdog = new Timer(_ => Environment.Exit(124), null, TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);
        var documentUrl = OperatingSystem.IsWindows() ? "http://ipc.localhost/index.html" : "ipc://app/index.html";
        using var app = new Builder().UseRuntime(runtime).Window("main", "NetWiv registered IPC protocol", documentUrl)
            .ResolveAssets(_ => new AssetResponse(404, "text/plain", "asset-fallback"u8.ToArray()))
            .RegisterUriSchemeProtocol("ipc", (_, request) =>
            {
                ProofGate.Check(!request.Headers.ContainsKey("netwiv-document-capability") &&
                    !request.Headers.ContainsKey("netwiv-document-id"), "Private transport headers reached a custom protocol handler.");
                var headers = new Dictionary<string, string> { ["Access-Control-Allow-Origin"] = "*", ["Access-Control-Allow-Headers"] = "*" };
                if (request.Method == "OPTIONS") return new(204, "", ReadOnlyMemory<byte>.Empty, headers);
                if (request.Uri.AbsolutePath == "/index.html") return new(200, "text/html", Encoding.UTF8.GetBytes(Page), headers);
                if (request.Uri.AbsolutePath != "/resource") return new(404, "text/plain", ReadOnlyMemory<byte>.Empty, headers);
                if (request.Method == "GET") Interlocked.Increment(ref resourceGets);
                if (request.Method == "POST") Interlocked.Increment(ref resourcePosts);
                return new(200, "text/plain", Encoding.UTF8.GetBytes("custom-" + request.Method), headers);
            })
            .RegisterCommand("echo", (_, args, _) =>
            {
                Interlocked.Increment(ref commandCalls);
                return ValueTask.FromResult(args.GetProperty("input").Clone());
            })
            .RegisterCommand("finish", (_, args, _) =>
            {
                passed = args.GetProperty("passed").GetBoolean();
                if (!passed) failure = args.GetProperty("error").GetString();
                desktop.Exit(passed ? 0 : 1);
                return ValueTask.FromResult(JsonValue.Null);
            }).Build();
        app.Run();
        ProofGate.Check(passed, "Registered IPC protocol assertions failed: " + failure);
        ProofGate.Check(resourceGets == 1 && resourcePosts == 1 && commandCalls == 1 && builtinCalls == 0,
            $"Custom protocol assets or authenticated message commands used the wrong route (GET={resourceGets}, POST={resourcePosts}, commands={commandCalls}, builtin={builtinCalls}).");
        Console.WriteLine("PASS registered ipc document, GET/POST assets and authenticated message commands without built-in fallback or private headers");
        return 0;
    }

    private sealed class ProbeRuntime(IDesktopRuntime inner, Action builtin) : IDesktopRuntime
    {
        public void Run(RuntimeContext context) => inner.Run(context with
        {
            ResolveUriScheme = request => request.Uri.AbsolutePath == "/unhandled"
                ? ValueTask.FromResult<AssetResponse?>(null) : context.ResolveUriScheme!(request),
            InvokeProtocol = request => { builtin(); return context.InvokeProtocol!(request); },
        });
        public void Dispatch(Action action) => inner.Dispatch(action);
        public Task<JsonElement> InvokeAsync(string command, JsonElement args, CancellationToken cancellationToken = default) => inner.InvokeAsync(command, args, cancellationToken);
        public Task<JsonElement> InvokeAsync(string command, JsonElement args, RuntimeCommandContext context, CancellationToken cancellationToken = default) => inner.InvokeAsync(command, args, context, cancellationToken);
        public Task<RuntimeInvokeResponse> InvokeResponseAsync(string command, JsonElement args, RuntimeCommandContext context, CancellationToken cancellationToken = default) => inner.InvokeResponseAsync(command, args, context, cancellationToken);
        public void EvaluateScript(string label, string script) => inner.EvaluateScript(label, script);
        public void EvaluateScript(string label, string script, ulong documentId) => inner.EvaluateScript(label, script, documentId);
        public void Exit(int code = 0) => inner.Exit(code);
    }

    private const string Page = """
        <!doctype html><script>
        (async () => {
          const assert = (value, message) => { if (!value) throw Error(message); };
          try {
            assert(window.__NETWIV_IPC__.protocolIpc === false, 'Claimed ipc still advertises the built-in protocol.');
            assert(await (await fetch('./resource')).text() === 'custom-GET', 'Custom GET failed.');
            assert(await (await fetch('./resource', { method: 'POST', body: 'custom', headers: {
              'NetWiv-Document-Capability': 'forged', 'NetWiv-Document-Id': '1'
            }})).text() === 'custom-POST', 'Custom POST failed.');
            if (window.__NETWIV_IPC__.fetchIpc) {
              let denied = false;
              try { await window.__NETWIV_IPC__.fetchIpc('ipc://localhost/resource', {}); } catch { denied = true; }
              assert(denied, 'Private credential-bearing fetch remained enabled for a custom scheme.');
            }
            const unhandled = await fetch('./unhandled', { method: 'POST', body: '{}' });
            assert(unhandled.status === 404 && await unhandled.text() === 'asset-fallback', 'Unhandled custom request reached built-in IPC.');
            assert(await window.__TAURI_INTERNALS__.invoke('echo', { input: 'message' }) === 'message', 'Authenticated message invocation failed.');
            await window.__TAURI_INTERNALS__.invoke('finish', { passed: true });
          } catch (error) {
            await window.__TAURI_INTERNALS__.invoke('finish', { passed: false, error: String(error) });
          }
        })();
        </script>
        """;
}
