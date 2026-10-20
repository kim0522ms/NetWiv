using System.Collections.Concurrent;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using NetWiv.BlazorHybrid;
using NetWiv.Runtime;

internal static class ManagerTests
{
    internal static async Task Dispatch()
    {
        var dispatcher = new BlazorDispatcher();
        var operations = new ConcurrentQueue<Task>();
        dispatcher.OperationStarted = operations.Enqueue;
        var count = 0;
        await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => dispatcher.InvokeAsync(() =>
        {
            Check.True(dispatcher.CheckAccess(), "Renderer work ran outside its dispatcher.");
            count++;
        }))));
        Check.Equal(32, count);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = dispatcher.InvokeAsync(async () =>
        {
            entered.TrySetResult();
            await release.Task;
            Check.True(dispatcher.CheckAccess(), "Asynchronous continuation lost renderer dispatcher access.");
        });
        await entered.Task;
        Check.True(!pending.IsCompleted && operations.Contains(pending), "Asynchronous renderer work was not observable for cleanup.");
        release.TrySetResult();
        await pending;
        await Check.ThrowsAsync<InvalidOperationException>(() => dispatcher.InvokeAsync(() =>
            throw new InvalidOperationException("Expected dispatcher failure.")));
    }

    internal static async Task Assets()
    {
        await using var fixture = new ManagerFixture();
        var host = fixture.Manager.TryGetResponse(Request("index.html"));
        Check.Equal(200, host!.StatusCode);
        Check.Contains("hybrid-test-root", Encoding.UTF8.GetString(host.Body.Span));
        var route = fixture.Manager.TryGetResponse(Request("nested/route"));
        Check.Equal(200, route!.StatusCode);
        Check.Equal(Encoding.UTF8.GetString(host.Body.Span), Encoding.UTF8.GetString(route.Body.Span));
        var css = fixture.Manager.TryGetResponse(Request("app.css"));
        Check.Equal(200, css!.StatusCode);
        Check.Contains("color: green", Encoding.UTF8.GetString(css.Body.Span));
        Check.Equal(404, fixture.Manager.TryGetResponse(Request("missing.js"))!.StatusCode);
        Check.True(fixture.Manager.TryGetResponse(Request("index.html", "https://foreign.invalid/")) is null,
            "A foreign origin received bundled application content.");
        fixture.Manager.Navigate("nested/route");
        Check.Equal(new Uri(fixture.Options.AppBaseUri, "nested/route"), fixture.Navigated);
        Check.Throws<InvalidOperationException>(() => fixture.Manager.Navigate("https://foreign.invalid/"));
    }

    internal static async Task ForeignOrigin()
    {
        await using var fixture = new ManagerFixture();
        await fixture.Manager.InitializeAsync();
        foreach (var source in new[] { "https://foreign.invalid/", "http://netwiv-blazor.localhost.evil.invalid/", "http://netwiv-blazor.localhost:8443/", "https://netwiv-blazor.localhost/" })
            fixture.Manager.ReceiveMessage(new(source), fixture.AttachMessage);
        await fixture.Manager.Dispatcher.InvokeAsync(() => { });
        Check.True(!await fixture.Manager.TryDispatchAsync(_ => throw new InvalidOperationException("Foreign page attached.")),
            "A foreign page attached a managed renderer.");
        Check.Equal(0, fixture.Probes.Count);
        Check.Equal(0, fixture.Messages.Count);
        fixture.Attach();
        Check.True(await fixture.Manager.TryDispatchAsync(_ => { }), "The bundled page did not attach.");
        Check.Equal(1, fixture.Probes.Count);
    }

    internal static async Task Replacement()
    {
        await using var fixture = new ManagerFixture();
        await fixture.Manager.InitializeAsync();
        fixture.Attach();
        ScopeProbe? first = null;
        Check.True(await fixture.Manager.TryDispatchAsync(services => first = services.GetRequiredService<ScopeProbe>()), "First page did not attach.");
        fixture.Attach();
        await first!.DisposalStarted.Task;
        Check.Equal(1, fixture.Probes.Count);
        Check.True(!first.Disposed, "Replacement did not await asynchronous scope disposal.");
        first.AllowDisposal.TrySetResult();
        await fixture.NextProbe.Task;
        ScopeProbe? current = null;
        Check.True(await fixture.Manager.TryDispatchAsync(services => current = services.GetRequiredService<ScopeProbe>()), "Replacement page did not attach.");
        Check.True(first.Disposed && !ReferenceEquals(first, current), "Replacement reused the retired page scope.");
    }

    internal static async Task Disposal()
    {
        await using var fixture = new ManagerFixture();
        await fixture.Manager.InitializeAsync();
        fixture.Attach();
        ScopeProbe? scope = null;
        Check.True(await fixture.Manager.TryDispatchAsync(services => scope = services.GetRequiredService<ScopeProbe>()), "Page did not attach.");
        var disposing = fixture.Manager.DisposeAsync().AsTask();
        await scope!.DisposalStarted.Task;
        Check.True(!disposing.IsCompleted && !scope.Disposed, "Manager did not await its page's asynchronous disposal.");
        scope.AllowDisposal.TrySetResult();
        await disposing;
        Check.True(scope.Disposed, "Page scope was not disposed.");
    }

    private static AssetRequest Request(string path, string origin = "http://netwiv-blazor.localhost/") =>
        new(new Uri(new Uri(origin), path), "GET", new Dictionary<string, string>());
}

internal sealed class ManagerFixture : IAsyncDisposable
{
    private readonly ServiceProvider _services;
    private readonly PhysicalFileProvider _files = new(Path.Combine(AppContext.BaseDirectory, "fixtures"));
    internal BlazorWebViewOptions Options { get; }
    internal BlazorWebViewManager Manager { get; }
    internal ConcurrentQueue<string> Messages { get; } = new();
    internal ConcurrentQueue<ScopeProbe> Probes { get; } = new();
    internal TaskCompletionSource NextProbe { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Uri? Navigated { get; private set; }
    internal string AttachMessage => $"__bwv:[\"AttachPage\",\"{Options.AppBaseUri}\",\"{Options.AppBaseUri}\"]";

    internal ManagerFixture()
    {
        var services = new ServiceCollection().AddLogging().AddNetWivBlazorHybrid();
        services.AddScoped(_ =>
        {
            var probe = new ScopeProbe();
            Probes.Enqueue(probe);
            if (Probes.Count > 1) NextProbe.TrySetResult();
            return probe;
        });
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        Options = new() { FileProvider = _files, HostPage = "index.html" };
        Options.RootComponents.Add<EmptyRoot>("#app");
        Manager = new(_services, Dispatcher.CreateDefault(), Options.AppBaseUri, Options,
            uri => Navigated = uri, Messages.Enqueue);
    }

    internal void Attach() => Manager.ReceiveMessage(Options.AppBaseUri, AttachMessage);

    public async ValueTask DisposeAsync()
    {
        foreach (var probe in Probes) probe.AllowDisposal.TrySetResult();
        await Manager.DisposeAsync();
        await _services.DisposeAsync();
        _files.Dispose();
    }
}

internal sealed class ScopeProbe : IAsyncDisposable
{
    internal TaskCompletionSource DisposalStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource AllowDisposal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal bool Disposed { get; private set; }
    internal TaskCompletionSource DisposedSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async ValueTask DisposeAsync()
    {
        DisposalStarted.TrySetResult();
        await AllowDisposal.Task;
        Disposed = true;
        DisposedSignal.TrySetResult();
    }
}

internal sealed class EmptyRoot : IComponent
{
    [Inject] public ScopeProbe Scope { get; set; } = null!;
    public void Attach(RenderHandle renderHandle) { }
    public Task SetParametersAsync(ParameterView parameters) => Task.CompletedTask;
}

internal static class Check
{
    internal static void True(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    internal static void Equal<T>(T expected, T actual) => True(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected '{expected}', got '{actual}'.");
    internal static void Contains(string expected, string actual) => True(actual.Contains(expected, StringComparison.Ordinal), $"Expected '{expected}' in response.");
    internal static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
    internal static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
