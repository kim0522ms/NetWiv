using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.JSInterop;
using NetWiv.BlazorHybrid;
using NetWiv.Runtime;

internal static class RetirementTests
{
    internal static async Task PendingAndFutureJs()
    {
        await using var fixture = new RetirementFixture();
        var old = await fixture.Attach(1);
        var pending = old.Invoke("pendingBeforeReload");
        var oldCall = await fixture.Message("BeginInvokeJS", 1, "pendingBeforeReload");
        fixture.Change(2);
        await Check.ThrowsAsync<JSException>(async () => await pending.WaitAsync(TimeSpan.FromSeconds(2)));
        await old.Scope.DisposedSignal.Task;
        var oldSends = fixture.Native.Scripts.Count(script => script.Document == 1);
        await Check.ThrowsAsync<JSException>(async () => await old.Invoke("calledAfterRetirement").WaitAsync(TimeSpan.FromSeconds(2)));
        Check.Equal(oldSends, fixture.Native.Scripts.Count(script => script.Document == 1));

        var current = await fixture.Attach(2, changeDocument: false);
        var fresh = current.Invoke("currentPageCall");
        var currentCall = await fixture.Message("BeginInvokeJS", 2, "currentPageCall");
        fixture.EndJs(1, oldCall[1].GetInt64(), "late old result");
        Check.True(!fresh.IsCompleted, "A retired document completed the current page's JS call.");
        fixture.EndJs(2, currentCall[1].GetInt64(), "current result");
        Check.Equal("current result", await fresh);
        Check.Equal(0, fixture.Native.ExitCodes.Count);
    }

    internal static async Task PendingRender()
    {
        await using var fixture = new RetirementFixture();
        var page = await fixture.Attach(1);
        await page.Render();
        await fixture.Message("RenderBatch", 1);
        // No OnRenderCompleted is returned: the native document has gone away.
        fixture.Change(1, closed: true);
        fixture.Native.Return.Set();
        await fixture.Run.WaitAsync(TimeSpan.FromSeconds(3));
        Check.True(page.Scope.Disposed, "An unacknowledged render prevented scoped-service cleanup.");
        Check.Equal(0, fixture.Native.ExitCodes.Count);
    }

    internal static async Task RapidReload()
    {
        await using var fixture = new RetirementFixture();
        var old = await fixture.Attach(1);
        var pending = old.Invoke("pendingBeforeRapidReload");
        await fixture.Message("BeginInvokeJS", 1, "pendingBeforeRapidReload");
        fixture.Change(2);
        fixture.Change(3);
        fixture.Change(1, closed: true);
        var current = await fixture.Attach(3, changeDocument: false);
        await Check.ThrowsAsync<JSException>(async () => await pending.WaitAsync(TimeSpan.FromSeconds(2)));
        var fresh = current.Invoke("currentAfterRapidReload");
        var call = await fixture.Message("BeginInvokeJS", 3, "currentAfterRapidReload");
        fixture.Receive(1, "invalid retired payload");
        fixture.EndJs(3, call[1].GetInt64(), "latest document");
        Check.Equal("latest document", await fresh);
        Check.True(!current.Scope.Disposed, "Retired-session cleanup disposed the latest page scope.");
        Check.Equal(0, fixture.Native.ExitCodes.Count);
    }
}

internal sealed class RetirementFixture : IAsyncDisposable
{
    private readonly ServiceProvider _services;
    private readonly PhysicalFileProvider _files = new(Path.Combine(AppContext.BaseDirectory, "fixtures"));
    private readonly Channel<(string Script, ulong Document)> _messages = Channel.CreateUnbounded<(string, ulong)>();
    private readonly PageCapture _capture = new();
    private readonly BlazorWebViewOptions _options;
    internal FakeRuntime Native { get; } = new();
    internal Task Run { get; }

    internal RetirementFixture()
    {
        var registrations = new ServiceCollection().AddLogging().AddNetWivBlazorHybrid();
        registrations.AddSingleton(_capture);
        registrations.AddScoped<ScopeProbe>();
        _services = registrations.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        _options = new() { FileProvider = _files, HostPage = "index.html" };
        _options.RootComponents.Add<CapturedRoot>("#app");
        Native.ScriptEvaluated = (script, document) => _messages.Writer.TryWrite((script, document));
        var runtime = new BlazorDesktopRuntime(Native, _services, _options);
        var context = new RuntimeContext([new("main", "Main", "index.html", 100, 100, default)], "", _ => { }, _ => { }, _ => null);
        Run = Task.Run(() => runtime.Run(context));
    }

    internal void Change(ulong document, bool closed = false) => Native.Context!.OnDocumentChanged!(new("main", document, closed));
    internal void Receive(ulong document, string message) => Native.Context!.OnMessage(new("main", _options.AppBaseUri,
        BlazorBridge.Prefix + message, "main", document));

    internal async Task<CapturedRoot> Attach(ulong document, bool changeDocument = true)
    {
        await Native.Ready.Task;
        if (changeDocument) Change(document);
        Receive(document, "__bwv:" + JsonSerializer.Serialize(new[] { "AttachPage", _options.AppBaseUri.AbsoluteUri, _options.AppBaseUri.AbsoluteUri }));
        return await _capture.Pages.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
    }

    internal async Task<JsonElement> Message(string kind, ulong document, string? identifier = null)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (true)
        {
            var message = await _messages.Reader.ReadAsync(timeout.Token);
            if (message.Document != document) continue;
            var argument = message.Script[(message.Script.IndexOf('(') + 1)..^2];
            var payload = JsonSerializer.Deserialize<string>(argument)!;
            using var parsed = JsonDocument.Parse(payload["__bwv:".Length..]);
            var values = parsed.RootElement;
            if (values[0].GetString() == kind && (identifier is null || values[2].GetString() == identifier))
                return values.Clone();
        }
    }

    internal void EndJs(ulong document, long handle, string result)
    {
        var completion = JsonSerializer.Serialize(new object[] { handle, true, result });
        Receive(document, "__bwv:" + JsonSerializer.Serialize(new object[] { "EndInvokeJS", handle, true, completion }));
    }

    public async ValueTask DisposeAsync()
    {
        Native.Return.Set();
        try { await Run.WaitAsync(TimeSpan.FromSeconds(7)); }
        finally
        {
            await _services.DisposeAsync();
            _files.Dispose();
            Native.Return.Dispose();
        }
    }
}

internal sealed class PageCapture
{
    internal Channel<CapturedRoot> Pages { get; } = Channel.CreateUnbounded<CapturedRoot>();
}

internal sealed class CapturedRoot : IComponent
{
    private RenderHandle _render;
    [Inject] public IJSRuntime JS { get; set; } = null!;
    [Inject] public ScopeProbe Scope { get; set; } = null!;
    [Inject] public PageCapture Capture { get; set; } = null!;
    public void Attach(RenderHandle renderHandle) => _render = renderHandle;
    public Task SetParametersAsync(ParameterView parameters)
    {
        Scope.AllowDisposal.TrySetResult();
        Capture.Pages.Writer.TryWrite(this);
        return Task.CompletedTask;
    }
    internal Task<string> Invoke(string identifier) => _render.Dispatcher.InvokeAsync(() => JS.InvokeAsync<string>(identifier).AsTask());
    internal Task Render() => _render.Dispatcher.InvokeAsync(() => _render.Render(builder => builder.AddContent(0, "pending render")));
}
