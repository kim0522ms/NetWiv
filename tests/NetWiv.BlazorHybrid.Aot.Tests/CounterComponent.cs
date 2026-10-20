using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using NetWiv.BlazorHybrid.Aot;

namespace NetWiv.BlazorHybrid.Aot.Tests;

public sealed partial class CounterComponent : ComponentBase, IAsyncDisposable
{
    private IJSObjectReference? _module;
    private BlazorAotObjectReference<InvokableReceiver>? _live;
    private BlazorAotObjectReference<InvokableReceiver>? _disposed;
    private BlazorAotObjectReference<InvokableReceiver>? _jsDisposed;
    private static readonly List<ProofScope> Scopes = [];
    public string Text { get; set; } = "initial";
    public bool Checked { get; set; }
    public string Selection { get; set; } = "first";
    [Inject] public IJSRuntime JS { get; set; } = null!;
    [Inject] public ProofScope Scope { get; set; } = null!;
    [Parameter] public string Label { get; set; } = "";
    [Parameter] public bool NativeInteropChecks { get; set; }
    public static int Count { get; private set; }
    public static string RenderedLabel { get; private set; } = "";
    public static ProbeDto? JsReply { get; private set; }
    public static ProofScope? CapturedScope { get; private set; }
    public static IJSRuntime? CapturedJS { get; private set; }
    public static bool Ready { get; private set; }

    public CounterComponent()
    {
        Count = 0;
        JsReply = null;
        Ready = false;
    }

    protected override void OnParametersSet()
    {
        RenderedLabel = Label;
#if NET9_0_OR_GREATER
        ProofGate.Check(RendererInfo.Name == "WebView" && RendererInfo.IsInteractive, "The renderer did not advertise interactive WebView execution.");
#endif
        CapturedScope = Scope;
        CapturedJS = JS;
        lock (Scopes) if (!Scopes.Contains(Scope)) Scopes.Add(Scope);
    }

    private static void Click(MouseEventArgs _) => Count++;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        JsReply = await JS.InvokeAsync<ProbeDto>("proof.dto", ProofData.Input());
        await JS.InvokeVoidAsync("proof.voidCall");
        _module = await JS.InvokeAsync<IJSObjectReference>("import", "./proof-module.js");
        await _module.InvokeVoidAsync("setFlag");
        _live = BlazorAotObjectReference.Create(new InvokableReceiver("instance:"));
        _disposed = BlazorAotObjectReference.Create(new InvokableReceiver("disposed:"));
        _jsDisposed = BlazorAotObjectReference.Create(new InvokableReceiver("js-disposed:"));
        await JS.InvokeVoidAsync("proof.setInstances", _live, _disposed, _jsDisposed);
        _disposed.Dispose();
        if (NativeInteropChecks) await VersionInteropProof.RunAsync(JS);
        Ready = true;
        await JS.InvokeVoidAsync("proof.ready");
        StateHasChanged();
    }

    public async ValueTask DisposeAsync()
    {
        _live?.Dispose();
        _disposed?.Dispose();
        _jsDisposed?.Dispose();
        if (_module is not null)
        {
            try { await _module.DisposeAsync(); }
            catch (JSDisconnectedException) { }
        }
    }

    internal static void AssertScopesDisposed(int expected)
    {
        lock (Scopes)
            ProofGate.Check(Scopes.Count == expected && Scopes.All(scope => scope.Disposed), "Page scopes were reused or left alive.");
    }
}

public sealed class ProofScope : IAsyncDisposable
{
    public bool Disposed { get; private set; }
    public async ValueTask DisposeAsync()
    {
        await Task.Yield();
        Disposed = true;
    }
}

internal static class ProofData
{
    internal static ProbeDto Input() => new()
    {
        Name = "input",
        Count = 3,
        Items = [new() { Text = "first", Value = long.MaxValue }, null, new() { Text = "last", Value = -5 }],
        Optional = null,
    };

    internal static ProbeDto Reply() => new()
    {
        Name = "reply",
        Count = 7,
        Items = [new() { Text = "nested reply", Value = long.MinValue }, null],
        Optional = new() { Text = "optional reply", Value = 42 },
    };
}
