using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using NetWiv;
using NetWiv.Blazor;
using NetWiv.Runtime;

internal static class RuntimeTests
{
    internal static async Task TransportBoundaries()
    {
        await using var fixture = new RuntimeFixture();
        var context = await fixture.Native.Ready.Task;
        Check.Equal(fixture.Options.AppBaseUri.AbsoluteUri, context.Windows[0].Url);
        Check.Equal("original.html", context.Windows[1].Url);
        Check.True(context.UriSchemeProtocols!.Contains("netwiv-blazor"), "Hybrid asset protocol was not registered.");
        Check.Contains(BlazorBridge.Script, context.InitializationScript);
        var original = new RuntimeMessage("other", new Uri("https://foreign.invalid/"), "ordinary message", "other", 8);
        context.OnMessage(original);
        Check.True(ReferenceEquals(original, fixture.Forwarded.Single()), "Ordinary IPC did not preserve its original context.");
        fixture.Change(1);
        await fixture.Asset(1);
        foreach (var message in new[]
        {
            fixture.Message(1) with { WebviewLabel = "other" },
            fixture.Message(0), fixture.Message(2),
            fixture.Message(1) with { Source = new Uri("https://foreign.invalid/") },
            fixture.Message(1) with { Source = new Uri("http://netwiv-blazor.localhost.evil.invalid/") },
            fixture.Message(1) with { Source = new Uri("http://netwiv-blazor.localhost:8443/") },
            fixture.Message(1) with { Source = new Uri("http://user@netwiv-blazor.localhost/") },
        }) context.OnMessage(message);
        // An awaited asset request drains earlier work on the same renderer dispatcher.
        await fixture.Asset(1);
        Check.Equal(0, fixture.Probes.Count);
        Check.Equal(0, fixture.Native.Scripts.Count);
        Check.Equal(403, (await fixture.Asset(1, "other"))!.StatusCode);
        Check.Equal(410, (await fixture.Asset(2))!.StatusCode);
        var scope = await fixture.Attach(1);
        Check.Equal(1, fixture.Probes.Count);
        Check.Equal(1UL, (await fixture.Native.ScriptReady.Task).Document);
        Check.True(!scope.Disposed, "Active page scope was disposed.");
        Check.Equal(0, fixture.Native.ExitCodes.Count);
    }

    internal static async Task Reload()
    {
        await using var fixture = new RuntimeFixture();
        await fixture.Native.Ready.Task;
        fixture.Change(1);
        var old = await fixture.Attach(1);
        fixture.Change(2);
        await old.DisposalStarted.Task;
        Check.True(!old.Disposed, "Reload skipped asynchronous page cleanup.");
        Check.Equal(410, (await fixture.Asset(1))!.StatusCode);
        fixture.Native.Context!.OnMessage(fixture.Message(1));
        fixture.Change(1, closed: true);
        var current = await fixture.Attach(2);
        Check.True(!ReferenceEquals(old, current), "Reload reused the old page's scope.");
        Check.Equal(200, (await fixture.Asset(2))!.StatusCode);
        old.AllowDisposal.TrySetResult();
        await old.DisposedSignal.Task;
        Check.True(!current.Disposed, "Late old-page cleanup disposed the current renderer.");
    }

    internal static async Task Close()
    {
        await using var fixture = new RuntimeFixture();
        await fixture.Native.Ready.Task;
        fixture.Change(1);
        var scope = await fixture.Attach(1);
        fixture.Change(1, closed: true);
        await scope.DisposalStarted.Task;
        Check.Equal(410, (await fixture.Asset(1))!.StatusCode);
        Check.Equal(410, (await fixture.Asset(0))!.StatusCode);
        fixture.Native.Context!.OnMessage(fixture.Message(1));
        scope.AllowDisposal.TrySetResult();
        await scope.DisposedSignal.Task;
        Check.Equal(1, fixture.Probes.Count);
        Check.Equal(410, (await fixture.Asset(1))!.StatusCode);
    }

    internal static async Task ExitAndReturn()
    {
        foreach (var explicitExit in new[] { false, true })
        {
            await using var fixture = new RuntimeFixture();
            await fixture.Native.Ready.Task;
            fixture.Change(1);
            var scope = await fixture.Attach(1);
            if (explicitExit) fixture.Runtime.Exit(7);
            fixture.Native.Return.Set();
            await scope.DisposalStarted.Task;
            Check.True(!fixture.Run.IsCompleted, "Runtime returned before asynchronous component cleanup finished.");
            Check.Equal(410, (await fixture.Asset(1))!.StatusCode);
            scope.AllowDisposal.TrySetResult();
            await fixture.Run;
            Check.True(scope.Disposed, "Runtime left its renderer scope alive.");
            if (explicitExit) Check.Equal(7, fixture.Native.ExitCodes.Single());
            else Check.Equal(0, fixture.Native.ExitCodes.Count);
        }
    }

    internal static async Task MalformedMessage()
    {
        await using var fixture = new RuntimeFixture(allowFailure: true);
        await fixture.Native.Ready.Task;
        fixture.Change(1);
        var scope = await fixture.Attach(1);
        fixture.Native.Context!.OnMessage(fixture.Message(1) with { Json = BlazorBridge.Prefix + "__bwv:invalid-json" });
        Check.Equal(1, await fixture.Native.Exited.Task);
        await scope.DisposalStarted.Task;
        fixture.Native.Return.Set();
        Check.True(!fixture.Run.IsCompleted, "Malformed-message failure abandoned asynchronous cleanup.");
        scope.AllowDisposal.TrySetResult();
        await Check.ThrowsAsync<JsonException>(async () => await fixture.Run);
        Check.True(scope.Disposed, "Failed runtime retained its renderer scope.");
    }
}

internal sealed class RuntimeFixture : IAsyncDisposable
{
    private readonly ServiceProvider _services;
    private readonly PhysicalFileProvider _files = new(Path.Combine(AppContext.BaseDirectory, "fixtures"));
    private readonly SemaphoreSlim _probeReady = new(0);
    private readonly ConcurrentQueue<ScopeProbe> _available = new();
    private readonly bool _allowFailure;
    internal ConcurrentQueue<ScopeProbe> Probes { get; } = new();
    internal ConcurrentQueue<RuntimeMessage> Forwarded { get; } = new();
    internal FakeRuntime Native { get; } = new();
    internal BlazorWebViewOptions Options { get; }
    internal BlazorDesktopRuntime Runtime { get; }
    internal Task Run { get; }

    internal RuntimeFixture(bool allowFailure = false)
    {
        _allowFailure = allowFailure;
        var services = new ServiceCollection().AddLogging().AddNetWivBlazor();
        services.AddScoped(_ =>
        {
            var probe = new ScopeProbe();
            Probes.Enqueue(probe);
            _available.Enqueue(probe);
            _probeReady.Release();
            return probe;
        });
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        Options = new() { FileProvider = _files, HostPage = "index.html" };
        Options.RootComponents.Add<EmptyRoot>("#app");
        Runtime = new(Native, _services, Options);
        var context = new RuntimeContext(
            [new("main", "Main", "original.html", 100, 100, default), new("other", "Other", "original.html", 100, 100, default)],
            "original initialization", Forwarded.Enqueue, _ => { }, _ => null);
        Run = Task.Run(() => Runtime.Run(context));
    }

    internal void Change(ulong document, bool closed = false) => Native.Context!.OnDocumentChanged!(new("main", document, closed));
    internal RuntimeMessage Message(ulong document) => new("main", Options.AppBaseUri,
        BlazorBridge.Prefix + $"__bwv:[\"AttachPage\",\"{Options.AppBaseUri}\",\"{Options.AppBaseUri}\"]", "main", document);

    internal async Task<ScopeProbe> Attach(ulong document)
    {
        Native.Context!.OnMessage(Message(document));
        await _probeReady.WaitAsync(TimeSpan.FromSeconds(3));
        return _available.TryDequeue(out var scope) ? scope : throw new InvalidOperationException("Page scope was not created.");
    }

    internal ValueTask<AssetResponse?> Asset(ulong document, string label = "main") =>
        Native.Context!.ResolveUriScheme!(new(new Uri("netwiv-blazor://localhost/index.html"), "GET",
            new Dictionary<string, string>(), WebviewLabel: label, WindowLabel: "main", DocumentId: document));

    public async ValueTask DisposeAsync()
    {
        foreach (var probe in Probes) probe.AllowDisposal.TrySetResult();
        Native.Return.Set();
        try { await Run.WaitAsync(TimeSpan.FromSeconds(6)); }
        catch when (_allowFailure) { }
        finally
        {
            await _services.DisposeAsync();
            _files.Dispose();
            _probeReady.Dispose();
            Native.Return.Dispose();
        }
    }
}

internal sealed class FakeRuntime : IDesktopRuntime
{
    internal RuntimeContext? Context { get; private set; }
    internal TaskCompletionSource<RuntimeContext> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<(string Script, ulong Document)> ScriptReady { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<int> Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal ManualResetEventSlim Return { get; } = new();
    internal ConcurrentQueue<(string Script, ulong Document)> Scripts { get; } = new();
    internal ConcurrentQueue<int> ExitCodes { get; } = new();
    internal Action<string, ulong>? ScriptEvaluated { get; set; }
    public void Run(RuntimeContext context)
    {
        Context = context;
        Ready.TrySetResult(context);
        context.OnReady?.Invoke();
        Return.Wait(TimeSpan.FromSeconds(15));
    }
    public void Dispatch(Action action) => action();
    public Task<JsonElement> InvokeAsync(string command, JsonElement args, CancellationToken cancellationToken = default) => Task.FromResult(JsonValue.Null);
    public void EvaluateScript(string webviewLabel, string script) => EvaluateScript(webviewLabel, script, 0);
    public void EvaluateScript(string webviewLabel, string script, ulong documentId)
    {
        Scripts.Enqueue((script, documentId));
        ScriptReady.TrySetResult((script, documentId));
        ScriptEvaluated?.Invoke(script, documentId);
    }
    public void Exit(int exitCode = 0)
    {
        ExitCodes.Enqueue(exitCode);
        Exited.TrySetResult(exitCode);
    }
}
