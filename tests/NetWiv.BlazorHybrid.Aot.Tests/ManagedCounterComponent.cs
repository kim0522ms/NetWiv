using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;


namespace NetWiv.BlazorHybrid.Aot.Tests;

public sealed partial class ManagedCounterComponent : ComponentBase, IAsyncDisposable
{
    private IJSObjectReference? _module;
    private DotNetObjectReference<InvokableReceiver>? _live;
    private DotNetObjectReference<InvokableReceiver>? _disposed;
    private DotNetObjectReference<InvokableReceiver>? _jsDisposed;
    private static readonly List<ProofScope> Scopes = [];
    public string Text { get; set; } = "initial";
    public bool Checked { get; set; }
    public string Selection { get; set; } = "first";
    [Inject] public IJSRuntime JS { get; set; } = null!;
    [Inject] public ProofScope Scope { get; set; } = null!;
    [Parameter] public string Label { get; set; } = "";
    public static int Count { get; private set; }
    public static string RenderedLabel { get; private set; } = "";
    public static ProbeDto? JsReply { get; private set; }
    public static ProofScope? CapturedScope { get; private set; }
    public static IJSRuntime? CapturedJS { get; private set; }
    public static bool Ready { get; private set; }

    public ManagedCounterComponent()
    {
        Count = 0;
        JsReply = null;
        Ready = false;
    }

    protected override void OnParametersSet()
    {
        RenderedLabel = Label;
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
        _live = DotNetObjectReference.Create(new InvokableReceiver("instance:"));
        _disposed = DotNetObjectReference.Create(new InvokableReceiver("disposed:"));
        _jsDisposed = DotNetObjectReference.Create(new InvokableReceiver("js-disposed:"));
        await JS.InvokeVoidAsync("proof.setInstances", _live, _disposed, _jsDisposed);
        _disposed.Dispose();
        await VersionInteropProof.RunAsync(JS);
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
