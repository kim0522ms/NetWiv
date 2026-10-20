using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using NetWiv.Runtime;

namespace NetWiv.BlazorHybrid.Aot.Tests;

internal static class RuntimeBoundaryTests
{
    internal static async Task RunAsync()
    {
        using var native = new BoundaryRuntime();
        var scopes = new ConcurrentQueue<BoundaryScope>();
        var bytesEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var faultEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBytes = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFault = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bytesFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var faultFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var services = new ServiceCollection()
            .AddScoped(_ => { var scope = new BoundaryScope(); scopes.Enqueue(scope); return scope; })
            .AddNetWivBlazorHybridAot(options =>
            {
                options.Components.Register<BoundaryComponent>();
                options.JsonTypeInfoResolver = ProofJsonContext.Default;
                options.Interop.RegisterStatic<byte[]>("boundary", "PendingBytes", async () =>
                {
                    bytesEntered.TrySetResult();
                    await releaseBytes.Task;
                    bytesFinished.TrySetResult();
                    return [1, 2, 3];
                }, ProofJsonContext.Default.ByteArray);
                options.Interop.RegisterStatic<int>("boundary", "PendingFault", async () =>
                {
                    faultEntered.TrySetResult();
                    try
                    {
                        await releaseFault.Task;
                        throw new InvalidOperationException("Expected late callback failure.");
                    }
                    finally { faultFinished.TrySetResult(); }
                }, ProofJsonContext.Default.Int32);
            }).BuildServiceProvider();
        var options = new BlazorWebViewOptions();
        options.RootComponents.Add<BoundaryComponent>("#app");
        var runtime = new BlazorDesktopRuntime(native, services, options);
        var forwarded = 0;
        var run = Task.Run(() => runtime.Run(new RuntimeContext(
            [new("main", "Boundary test", "index.html", 100, 100, default)],
            "", _ => Interlocked.Increment(ref forwarded), _ => { }, _ => null)));
        try
        {
            var context = await native.Ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
            context.OnDocumentChanged!(new("main", 1, false));
            await Asset(1);
            var valid = new RuntimeMessage("main", options.AppBaseUri,
                BlazorBridge.Prefix + "__bwv:[\"AttachPage\",\"http://netwiv-blazor.localhost/\",\"http://netwiv-blazor.localhost/\"]", "main", 1);
            foreach (var invalid in new[]
            {
                valid with { WebviewLabel = "other" },
                valid with { DocumentId = 0 }, valid with { DocumentId = 2 },
                valid with { Source = new Uri("https://foreign.invalid/") },
                valid with { Source = new Uri("http://netwiv-blazor.localhost.evil.invalid/") },
                valid with { Source = new Uri("http://netwiv-blazor.localhost:8443/") },
                valid with { Source = new Uri("http://user@netwiv-blazor.localhost/") },
            }) context.OnMessage(invalid);
            await Asset(1);
            ProofGate.Check(scopes.IsEmpty && native.Sent == 0, "A foreign or stale page reached the AOT renderer.");
            context.OnMessage(valid with { Json = "ordinary application message" });
            ProofGate.Check(forwarded == 1, "The adapter intercepted ordinary application IPC.");
            context.OnMessage(valid);
            await PumpUntilAsync(1, () => scopes.TryPeek(out var scope) && scope.Rendered.Task.IsCompleted);
            ProofGate.Check(scopes.TryDequeue(out var old), "The first page scope was not created.");
            var pending = old!.JS!.InvokeAsync<int>("neverCompletes", []).AsTask();
            context.OnDocumentChanged!(new("main", 2, false));
            await ExpectDisconnected(() => pending);
            await ExpectDisconnected(() => old.JS.InvokeAsync<int>("retiredMustNotSend", []).AsTask());
            await old.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            ProofGate.Check((await Asset(1))?.StatusCode == 410, "Retired document assets remained available.");
            context.OnMessage(valid);
            context.OnMessage(valid with { DocumentId = 2 });
            await PumpUntilAsync(2, () => scopes.TryPeek(out var scope) && scope.Rendered.Task.IsCompleted);
            ProofGate.Check(scopes.TryDequeue(out var current) && !ReferenceEquals(old, current), "Reload reused its page scope.");
            foreach (var method in new[] { "PendingBytes", "PendingFault" })
                context.OnMessage(valid with { DocumentId = 2, Json = BlazorBridge.Prefix + ProtocolFixture.Message("BeginInvokeDotNet", writer =>
                {
                    writer.WriteStringValue(method);
                    writer.WriteStringValue("boundary");
                    writer.WriteStringValue(method);
                    writer.WriteNumberValue(0);
                    writer.WriteStringValue("[]");
                }) });
            await Task.WhenAll(bytesEntered.Task, faultEntered.Task).WaitAsync(TimeSpan.FromSeconds(5));
            context.OnDocumentChanged!(new("main", 2, true));
            context.OnMessage(valid with { DocumentId = 2 });
            await current!.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Asset(2);
            ProofGate.Check(scopes.IsEmpty && (await Asset(2))?.StatusCode == 410, "A closed document recreated its page scope.");
            native.Return.Set();
            await run.WaitAsync(TimeSpan.FromSeconds(5));
            ProofGate.Check(!releaseBytes.Task.IsCompleted && !releaseFault.Task.IsCompleted,
                "Shutdown waited for application callback completion.");
            var sendsAfterClose = native.Sent;
            releaseBytes.TrySetResult();
            releaseFault.TrySetResult();
            await Task.WhenAll(bytesFinished.Task, faultFinished.Task).WaitAsync(TimeSpan.FromSeconds(5));
            // Allow the observed detached continuations to serialize or reject their result.
            await Task.Delay(50);
            ProofGate.Check(native.Sent == sendsAfterClose && run.IsCompletedSuccessfully,
                "A retired callback sent bytes/results or failed the already-closed runtime.");
            Console.WriteLine("PASS product adapter origin/label/document filtering, reload retirement, pending JS cancellation and closed-page rejection");
            Console.WriteLine("PASS close detaches pending JS-invokable work and observes late failure without sending late byte results");

            ValueTask<AssetResponse?> Asset(ulong document) => context.ResolveUriScheme!(new(
                new Uri("netwiv-blazor://localhost/index.html"), "GET", new Dictionary<string, string>(),
                WebviewLabel: "main", WindowLabel: "main", DocumentId: document));

            async Task PumpUntilAsync(ulong documentId, Func<bool> completed)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                while (!completed())
                {
                    var (message, document) = await native.Outgoing.Reader.ReadAsync(timeout.Token);
                    if (document != documentId) continue;
                    using var parsed = JsonDocument.Parse(message.AsMemory("__bwv:".Length));
                    var values = parsed.RootElement;
                    string? reply = values[0].GetString() switch
                    {
                        "BeginInvokeJS" => ProtocolFixture.Message("EndInvokeJS", writer =>
                        {
                            var id = values[1].GetInt64();
                            writer.WriteNumberValue(id);
                            writer.WriteBooleanValue(true);
                            writer.WriteStringValue("[" + id + ",true,null]");
                        }),
                        "RenderBatch" => ProtocolFixture.Message("OnRenderCompleted", writer =>
                        { writer.WriteNumberValue(values[1].GetInt64()); writer.WriteNullValue(); }),
                        "AttachToDocument" => null,
                        _ => throw new InvalidOperationException("Unexpected boundary-test message.")
                    };
                    if (reply is not null) context.OnMessage(valid with { DocumentId = documentId, Json = BlazorBridge.Prefix + reply });
                    // An asset request drains work on the same product dispatcher.
                    await Asset(documentId);
                    if (values[0].GetString() == "RenderBatch" && scopes.TryPeek(out var rendered))
                        await rendered.Rendered.Task.WaitAsync(timeout.Token);
                }
            }
        }
        finally
        {
            releaseBytes.TrySetResult();
            releaseFault.TrySetResult();
            native.Return.Set();
            await run.WaitAsync(TimeSpan.FromSeconds(6));
        }
        await MalformedCurrentDocumentAsync();
    }

    private static async Task MalformedCurrentDocumentAsync()
    {
        using var native = new BoundaryRuntime();
        var created = new TaskCompletionSource<BoundaryScope>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var services = new ServiceCollection()
            .AddScoped(_ => { var scope = new BoundaryScope(); created.TrySetResult(scope); return scope; })
            .AddNetWivBlazorHybridAot(options => options.Components.Register<BoundaryComponent>()).BuildServiceProvider();
        var options = new BlazorWebViewOptions();
        options.RootComponents.Add<BoundaryComponent>("#app");
        var runtime = new BlazorDesktopRuntime(native, services, options);
        var run = Task.Run(() => runtime.Run(new RuntimeContext(
            [new("main", "Malformed test", "index.html", 100, 100, default)], "", _ => { }, _ => { }, _ => null)));
        try
        {
            var context = await native.Ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var incoming = new RuntimeMessage("main", options.AppBaseUri, "", "main", 1);
            native.ScriptSent = (message, _) =>
            {
                using var parsed = JsonDocument.Parse(message.AsMemory("__bwv:".Length));
                var values = parsed.RootElement;
                string? reply = values[0].GetString() switch
                {
                    "BeginInvokeJS" => ProtocolFixture.Message("EndInvokeJS", writer =>
                    {
                        var id = values[1].GetInt64();
                        writer.WriteNumberValue(id); writer.WriteBooleanValue(true);
                        writer.WriteStringValue("[" + id + ",true,null]");
                    }),
                    "RenderBatch" => ProtocolFixture.Message("OnRenderCompleted", writer =>
                    { writer.WriteNumberValue(values[1].GetInt64()); writer.WriteNullValue(); }),
                    _ => null
                };
                if (reply is not null) context.OnMessage(incoming with { Json = BlazorBridge.Prefix + reply });
            };
            context.OnDocumentChanged!(new("main", 1, false));
            context.OnMessage(incoming with { Json = BlazorBridge.Prefix + "__bwv:[\"AttachPage\",\"http://netwiv-blazor.localhost/\",\"http://netwiv-blazor.localhost/\"]" });
            var scope = await created.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await scope.Rendered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            context.OnMessage(incoming with { Json = BlazorBridge.Prefix + "__bwv:{" });
            var rejected = false;
            try { await run.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (JsonException) { rejected = true; }
            ProofGate.Check(rejected && native.ExitCode == 1 && scope.Disposed.Task.IsCompletedSuccessfully,
                "Malformed current-page IPC did not fail through complete asynchronous scope cleanup.");
            Console.WriteLine("PASS malformed current-document IPC triggers controlled exit and awaits asynchronous scope cleanup");
        }
        finally
        {
            native.Return.Set();
            try { await run.WaitAsync(TimeSpan.FromSeconds(6)); }
            catch (JsonException) { }
        }
    }

    private static async Task ExpectDisconnected(Func<Task> operation)
    {
        try { await operation().WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (JSDisconnectedException) { return; }
        throw new InvalidOperationException("A retired runtime did not reject JavaScript interop.");
    }
}

public sealed class BoundaryComponent : ComponentBase
{
    [Inject] public BoundaryScope Scope { get; set; } = null!;
    [Inject] public IJSRuntime JS { get; set; } = null!;
    protected override void OnInitialized() => Scope.JS = JS;
    protected override void OnAfterRender(bool firstRender) => Scope.Rendered.TrySetResult();
}

public sealed class BoundaryScope : IAsyncDisposable
{
    public IJSRuntime? JS { get; set; }
    public TaskCompletionSource Rendered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async ValueTask DisposeAsync() { await Task.Yield(); Disposed.TrySetResult(); }
}

internal sealed class BoundaryRuntime : IDesktopRuntime, IDisposable
{
    internal ManualResetEventSlim Return { get; } = new();
    internal TaskCompletionSource<RuntimeContext> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal System.Threading.Channels.Channel<(string Message, ulong Document)> Outgoing { get; } = System.Threading.Channels.Channel.CreateUnbounded<(string, ulong)>();
    internal int Sent;
    internal int? ExitCode;
    internal Action<string, ulong>? ScriptSent { get; set; }
    public void Run(RuntimeContext context) { Ready.TrySetResult(context); Return.Wait(TimeSpan.FromSeconds(20)); }
    public void Dispatch(Action action) => action();
    public Task<JsonElement> InvokeAsync(string command, JsonElement args, CancellationToken cancellationToken = default) => Task.FromResult(default(JsonElement));
    public void EvaluateScript(string label, string script) => EvaluateScript(label, script, 0);
    public void EvaluateScript(string label, string script, ulong documentId)
    {
        const string prefix = "window.__netwivBlazorReceiveMessage(";
        ProofGate.Check(label == "main" && script.StartsWith(prefix, StringComparison.Ordinal) && script.EndsWith(");", StringComparison.Ordinal), "Unexpected product script wrapper.");
        var message = JsonSerializer.Deserialize(script[prefix.Length..^2], ProofJsonContext.Default.String)!;
        Interlocked.Increment(ref Sent);
        Outgoing.Writer.TryWrite((message, documentId));
        ScriptSent?.Invoke(message, documentId);
    }
    public void Exit(int exitCode = 0) { ExitCode = exitCode; Return.Set(); }
    public void Dispose() => Return.Dispose();
}
