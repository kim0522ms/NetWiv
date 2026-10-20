// Adapted from the .NET Foundation's WebRenderer/WebViewRenderer/PageContext
// at aspnetcore commit 7413d24330774263a78a4f3837ca53493518455a, MIT licensed.
#pragma warning disable BL0006 // Platform implementation of the official Renderer transport.
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using NetWiv.BlazorHybrid.Aot.Interop;

namespace NetWiv.BlazorHybrid.Aot.Hosting;

// A native document owns one session. Reload creates another session and scope;
// this type never reuses a renderer or interop identity across documents.
internal sealed class AotWebViewSession : IAsyncDisposable
{
    private readonly IServiceProvider _services;
    private readonly BlazorDispatcher _dispatcher;
    private readonly Uri _baseUri;
    private readonly BlazorRootComponent[] _roots;
    private readonly BlazorAotComponentRegistry _components;
    private readonly Action<string> _send;
    private readonly Action<Exception> _fail;
    private readonly AotJsRuntime _runtime;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _disposeGate = new();
    private AsyncServiceScope? _scope;
    private AotWebViewRenderer? _renderer;
    private AotNavigationManager? _navigation;
    private Task? _disposeTask;
    private Exception? _failure;
    private bool _attaching;
    private int _disconnected;

    internal AotWebViewSession(IServiceProvider services, BlazorDispatcher dispatcher, Uri baseUri,
        BlazorWebViewOptions webViewOptions, BlazorAotOptions options, Action<string> send, Action<Exception> fail)
    {
        _services = services;
        _dispatcher = dispatcher;
        _baseUri = baseUri;
        _roots = webViewOptions.RootComponents.ToArray();
        _components = options.Components.Snapshot();
        _send = send;
        _fail = fail;
        _runtime = new(send, options.JsonTypeInfoResolver, options.Interop);
        _runtime.RegisterInstanceRaw(1, "DispatchEventAsync", DispatchEventAsync);
    }

    internal Task Ready => _ready.Task;
    internal AotJsRuntime JSRuntime => _runtime;

    internal Task InitializeAsync() => _dispatcher.InvokeAsync(() =>
    {
        foreach (var root in _roots)
            if (!_components.Contains(root.ComponentType))
                throw new InvalidOperationException($"Root component '{root.ComponentType}' is not registered in BlazorAotOptions.Components.");
    });

    internal async Task ReceiveAsync(Uri sourceUri, string message)
    {
        if (Volatile.Read(ref _disconnected) != 0 || !sourceUri.IsAbsoluteUri || sourceUri.UserInfo.Length != 0 ||
            !_baseUri.IsBaseOf(sourceUri)) return;
        if (!message.StartsWith("__bwv:", StringComparison.Ordinal)) return;
        using var document = JsonDocument.Parse(message.AsMemory("__bwv:".Length));
        var envelope = document.RootElement;
        if (envelope.ValueKind != JsonValueKind.Array || envelope.GetArrayLength() == 0)
            throw new JsonException("A Blazor message must be a nonempty array.");
        await _dispatcher.InvokeAsync(async () =>
        {
            if (Volatile.Read(ref _disconnected) != 0) return;
            switch (envelope[0].GetString())
            {
                case "AttachPage":
                    RequireLength(envelope, 3);
                    await AttachPageAsync(envelope[1].GetString()!, envelope[2].GetString()!);
                    break;
                case "OnRenderCompleted":
                    RequireLength(envelope, 3);
                    if (envelope[2].ValueKind != JsonValueKind.Null) throw new InvalidOperationException(envelope[2].GetString());
                    Renderer.Acknowledge(envelope[1].GetInt64());
                    break;
                case "OnLocationChanged":
                    RequireLength(envelope, 4);
                    Navigation.LocationUpdated(envelope[1].GetString()!, envelope[2].GetString(), envelope[3].GetBoolean());
                    break;
                case "OnLocationChanging":
                    RequireLength(envelope, 5);
                    await Navigation.LocationChangingAsync(envelope[1].GetInt32(), envelope[2].GetString()!, envelope[3].GetString(), envelope[4].GetBoolean());
                    break;
                case "BeginInvokeDotNet":
                case "EndInvokeJS":
                case "ReceiveByteArrayFromJS":
                    _ = Renderer; // A page must be attached before invoking .NET or completing JS.
                    await _runtime.ReceiveAsync(envelope);
                    break;
                default:
                    throw new NotSupportedException("Unsupported Blazor WebView message type.");
            }
        });
    }

    private AotWebViewRenderer Renderer => _renderer ?? throw new InvalidOperationException("No Blazor page is attached.");
    private AotNavigationManager Navigation => _navigation ?? throw new InvalidOperationException("No Blazor page is attached.");

    private async Task AttachPageAsync(string baseUri, string initialUri)
    {
        if (_attaching) throw new InvalidOperationException("A Blazor session can attach only one native document.");
        if (!Uri.TryCreate(baseUri, UriKind.Absolute, out var parsedBase) || parsedBase != _baseUri)
            throw new InvalidOperationException("The Blazor page base URI differs from the configured application base URI.");
        _attaching = true;
        try
        {
            _scope = _services.CreateAsyncScope();
            var services = _scope.Value.ServiceProvider;
            _navigation = new(_baseUri, initialUri, _dispatcher, _send, ReportFailure);
            // Bind before resolving application services or the component activator.
            services.GetRequiredService<AotPageServices>().Configure(_runtime, _navigation);
            _renderer = new(services, services.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance,
                _components.CreateActivator(services), _dispatcher, _runtime, _send, ReportFailure);
            using var reference = JsonDocument.Parse("{\"__dotNetObject\":1}");
            using var empty = JsonDocument.Parse("{}");
            // WebRendererId.WebView = 3 in the pinned C# and JavaScript.
            await _runtime.InvokeVoidAsync("Blazor._internal.attachWebRendererInterop", 3,
                reference.RootElement.Clone(), empty.RootElement.Clone(), empty.RootElement.Clone());
            foreach (var root in _roots)
            {
                ThrowIfDisconnected();
                await _renderer.AddRootAsync(root.ComponentType, root.Selector,
                    ParameterView.FromDictionary(new Dictionary<string, object?>(root.Parameters)));
            }
            ThrowIfDisconnected();
            if (_failure is { } failure) throw new InvalidOperationException("Blazor page initialization failed.", failure);
            _ready.TrySetResult();
        }
        catch (Exception error)
        {
            if (Volatile.Read(ref _disconnected) != 0 && IsDisconnectFailure(error)) _ready.TrySetCanceled();
            else _ready.TrySetException(error);
            throw;
        }
    }

    private async ValueTask<string> DispatchEventAsync(JsonElement arguments)
    {
        RequireLength(arguments, 2);
        var data = AotEventData.Read(arguments[0], arguments[1]);
        await Renderer.DispatchEventAsync(data.HandlerId, data.FieldInfo, data.Arguments);
        return "null";
    }

    internal void Disconnect()
    {
        if (Interlocked.Exchange(ref _disconnected, 1) != 0) return;
        _runtime.Disconnect();
        _ready.TrySetCanceled();
        _ = _dispatcher.InvokeAsync(() => _renderer?.CancelPendingBatches());
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate) return new(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        Disconnect();
        try
        {
            await _dispatcher.InvokeAsync(async () =>
            {
                if (_renderer is not null)
                {
                    _renderer.CancelPendingBatches();
                    try { await _renderer.DisposeAsync(); }
                    catch (Exception error) when (IsDisconnectFailure(error)) { }
                }
            });
        }
        finally
        {
            try { if (_scope is { } scope) await scope.DisposeAsync(); }
            finally
            {
                _scope = null;
                _renderer = null;
                _navigation = null;
                _runtime.Dispose();
            }
        }
    }

    private void ReportFailure(Exception error)
    {
        if (Volatile.Read(ref _disconnected) != 0 && IsDisconnectFailure(error)) return;
        Interlocked.CompareExchange(ref _failure, error, null);
        _ready.TrySetException(error);
        _fail(error);
    }

    private void ThrowIfDisconnected()
    {
        if (Volatile.Read(ref _disconnected) != 0) throw new JSDisconnectedException("The Blazor document is disconnected.");
    }

    internal static bool IsDisconnectFailure(Exception error) => error switch
    {
        JSDisconnectedException or TaskCanceledException => true,
        AggregateException aggregate => aggregate.InnerExceptions.Count > 0 && aggregate.InnerExceptions.All(IsDisconnectFailure),
        _ => false
    };

    private static void RequireLength(JsonElement array, int length)
    {
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() != length)
            throw new JsonException("Invalid Blazor message argument count.");
    }
}
