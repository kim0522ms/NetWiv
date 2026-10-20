using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.JSInterop;
using System.Text.Json;
using NetWiv.BlazorHybrid;
using NetWiv.Runtime;

internal static class JsDisposalTests
{
    internal static async Task TrackingBound()
    {
        await using var fixture = new ManagerFixture();
        await fixture.Manager.InitializeAsync();
        fixture.Attach();
        Check.True(await fixture.Manager.TryDispatchAsync(_ => { }), "The tracking-bound test page did not attach.");
        var transport = new BlazorSessionTransport(_ => { });
        transport.Attach(fixture.Manager);
        for (var id = 1; id <= BlazorSessionTransport.MaximumTrackedJsCalls; id++)
            await transport.SendAsync($"__bwv:[\"BeginInvokeJS\",{id},\"test\"]");
        // Catching and retrying rejected IDs must not grow the tracked set or
        // consume the slot released by a real completion.
        for (var id = 4097; id <= 4100; id++)
            await Check.ThrowsAsync<InvalidOperationException>(() => transport.SendAsync($"__bwv:[\"BeginInvokeJS\",{id},\"test\"]"));
        // Microsoft's receiver uses the nested ID even if the outer one differs.
        transport.Received("__bwv:[\"EndInvokeJS\",999,true,\"[1,true,null]\"]");
        await transport.SendAsync("__bwv:[\"BeginInvokeJS\",4097,\"test\"]");
        await Check.ThrowsAsync<InvalidOperationException>(() => transport.SendAsync("__bwv:[\"BeginInvokeJS\",4098,\"test\"]"));
        transport.Retire();
        Check.True(!transport.IsExpectedDisconnect(new AggregateException()), "An unrelated empty aggregate was hidden during retirement.");
    }

    internal static async Task InlineDispatcherFailure()
    {
        var dispatcher = new BlazorDispatcher();
        await dispatcher.InvokeAsync(async () =>
        {
            // Upstream NotifyUnhandledException intentionally discards this task.
            // It must not throw inline through renderer disposal and skip DI cleanup.
            var failure = dispatcher.InvokeAsync((Action)(() => throw new InvalidOperationException("Expected inline dispatcher failure.")));
            Check.True(failure.IsFaulted, "An inline failure was not represented by its returned task.");
            await Check.ThrowsAsync<InvalidOperationException>(() => failure);
        });
    }

    internal static async Task ModuleReference()
    {
        using var files = new PhysicalFileProvider(Path.Combine(AppContext.BaseDirectory, "fixtures"));
        using var state = new DisposalState();
        var registrations = new ServiceCollection().AddLogging().AddNetWivBlazorHybrid();
        registrations.AddSingleton(state);
        registrations.AddScoped<ScopeProbe>();
        await using var services = registrations.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var options = new BlazorWebViewOptions { FileProvider = files, HostPage = "index.html" };
        options.RootComponents.Add<ModuleDisposalRoot>("#app");
        var native = new FakeRuntime();
        native.ScriptEvaluated = (script, documentId) =>
        {
            var argument = script[(script.IndexOf('(') + 1)..^2];
            var payload = JsonSerializer.Deserialize<string>(argument)!;
            using var parsed = JsonDocument.Parse(payload["__bwv:".Length..]);
            var values = parsed.RootElement;
            if (values[0].GetString() != "BeginInvokeJS" || values[2].GetString() != "import") return;
            var taskId = values[1].GetInt64();
            var result = JsonSerializer.Serialize(new object[] { taskId, true, new { __jsObjectId = 17 } });
            native.Context!.OnMessage(new("main", options.AppBaseUri, BlazorBridge.Prefix + "__bwv:" +
                JsonSerializer.Serialize(new object[] { "EndInvokeJS", taskId, true, result }), "main", documentId));
        };
        var runtime = new BlazorDesktopRuntime(native, services, options);
        var context = new RuntimeContext([new("main", "Main", "index.html", 100, 100, default)],
            "", _ => { }, _ => { }, _ => null);
        var run = Task.Run(() => runtime.Run(context));
        try
        {
            var callbacks = await native.Ready.Task;
            callbacks.OnDocumentChanged!(new("main", 1, false));
            callbacks.OnMessage(new("main", options.AppBaseUri, BlazorBridge.Prefix +
                $"__bwv:[\"AttachPage\",\"{options.AppBaseUri}\",\"{options.AppBaseUri}\"]", "main", 1));
            await state.Attached.Task.WaitAsync(TimeSpan.FromSeconds(3));
            callbacks.OnDocumentChanged!(new("main", 1, true));
            native.Return.Set();
            await run.WaitAsync(TimeSpan.FromSeconds(7));
            Check.True(state.Scope!.Disposed, "An actual JS module's disposal skipped scoped-service cleanup.");
            Check.True(state.DisposalStarted.Task.IsCompleted, "The module component was not disposed.");
            Check.Equal(0, native.ExitCodes.Count);
        }
        finally
        {
            native.Return.Set();
            if (state.Scope is { } scope) scope.AllowDisposal.TrySetResult();
            try { await run.WaitAsync(TimeSpan.FromSeconds(7)); }
            catch { } // Preserve the failure from the scenario itself.
            native.Return.Dispose();
        }
    }

    // This uses a cancellable JS call only to recover the test resources after
    // detecting a shutdown failure. Cancellation is never requested during Run.
    internal static async Task RetiredPageInterop()
    {
        using var files = new PhysicalFileProvider(Path.Combine(AppContext.BaseDirectory, "fixtures"));
        using var state = new DisposalState();
        var registrations = new ServiceCollection().AddLogging().AddNetWivBlazorHybrid();
        registrations.AddSingleton(state);
        registrations.AddScoped<ScopeProbe>();
        await using var services = registrations.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var options = new BlazorWebViewOptions { FileProvider = files, HostPage = "index.html" };
        options.RootComponents.Add<JsDisposalRoot>("#app");
        var native = new FakeRuntime();
        var runtime = new BlazorDesktopRuntime(native, services, options);
        var context = new RuntimeContext([new("main", "Main", "index.html", 100, 100, default)],
            "", _ => { }, _ => { }, _ => null);
        var run = Task.Run(() => runtime.Run(context));
        Exception? failure = null;
        bool disposedBeforeRecovery;
        try
        {
            var callbacks = await native.Ready.Task;
            callbacks.OnDocumentChanged!(new("main", 1, false));
            callbacks.OnMessage(new("main", options.AppBaseUri, BlazorBridge.Prefix +
                $"__bwv:[\"AttachPage\",\"{options.AppBaseUri}\",\"{options.AppBaseUri}\"]", "main", 1));
            await state.Attached.Task;
            callbacks.OnDocumentChanged!(new("main", 1, true));
            await state.DisposalStarted.Task;
            native.Return.Set();
            try { await run.WaitAsync(TimeSpan.FromSeconds(7)); }
            catch (Exception error) { failure = error; }
            disposedBeforeRecovery = state.Scope!.Disposed;
        }
        finally
        {
            // A lost JS callback must not leave this regression's scope alive.
            // This recovery happens only after Run has completed or timed out.
            state.JsCancellation.Cancel();
            native.Return.Set();
            if (state.Scope is { } scope)
            {
                scope.AllowDisposal.TrySetResult();
                try { await scope.DisposedSignal.Task.WaitAsync(TimeSpan.FromSeconds(3)); }
                catch (Exception recoveryError)
                { failure = new AggregateException("JS disposal recovery failed.", failure ?? recoveryError, recoveryError); }
            }
            try { await run.WaitAsync(TimeSpan.FromSeconds(7)); }
            catch { } // The assertion below reports the original shutdown failure.
            native.Return.Dispose();
        }
        Check.True(failure is null,
            $"Retired-page JS disposal prevented clean shutdown: {failure?.GetType().Name}: {failure?.Message}; scoped services disposed before test recovery: {disposedBeforeRecovery}.");
        Check.True(disposedBeforeRecovery, "Retired-page JS disposal left its scoped services alive.");
    }

    private sealed class DisposalState : IDisposable
    {
        internal TaskCompletionSource Attached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource DisposalStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationTokenSource JsCancellation { get; } = new();
        internal ScopeProbe? Scope { get; set; }
        public void Dispose() => JsCancellation.Dispose();
    }

    private sealed class JsDisposalRoot : IComponent, IAsyncDisposable
    {
        [Inject] public IJSRuntime JS { get; set; } = null!;
        [Inject] public ScopeProbe Scope { get; set; } = null!;
        [Inject] public DisposalState State { get; set; } = null!;
        public void Attach(RenderHandle renderHandle) { }
        public Task SetParametersAsync(ParameterView parameters)
        {
            State.Scope = Scope;
            Scope.AllowDisposal.TrySetResult();
            State.Attached.TrySetResult();
            return Task.CompletedTask;
        }
        public async ValueTask DisposeAsync()
        {
            State.DisposalStarted.TrySetResult();
            try { await JS.InvokeVoidAsync("disposeTest", State.JsCancellation.Token); }
            catch (OperationCanceledException) when (State.JsCancellation.IsCancellationRequested) { }
        }
    }

    private sealed class ModuleDisposalRoot : IComponent, IAsyncDisposable
    {
        private IJSObjectReference? _module;
        [Inject] public IJSRuntime JS { get; set; } = null!;
        [Inject] public ScopeProbe Scope { get; set; } = null!;
        [Inject] public DisposalState State { get; set; } = null!;
        public void Attach(RenderHandle renderHandle) { }
        public async Task SetParametersAsync(ParameterView parameters)
        {
            State.Scope = Scope;
            Scope.AllowDisposal.TrySetResult();
            _module = await JS.InvokeAsync<IJSObjectReference>("import", "./test-module.js");
            State.Attached.TrySetResult();
        }
        public async ValueTask DisposeAsync()
        {
            State.DisposalStarted.TrySetResult();
            if (_module is not null) await _module.DisposeAsync();
        }
    }
}
