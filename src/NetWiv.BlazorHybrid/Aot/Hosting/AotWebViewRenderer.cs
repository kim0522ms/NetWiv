// Adapted from the .NET Foundation's WebRenderer.cs and WebViewRenderer.cs at
// aspnetcore commit 7413d24330774263a78a4f3837ca53493518455a, licensed under MIT.
// The official Renderer owns component state, event dispatch and diffing.
#pragma warning disable BL0006 // Platform implementation of the official Renderer transport.
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.Logging;
using NetWiv.BlazorHybrid.Aot.Interop;

namespace NetWiv.BlazorHybrid.Aot.Hosting;

internal sealed class AotWebViewRenderer : Renderer
{
    private readonly Dispatcher _dispatcher;
    private readonly Action<string> _send;
    private readonly Action<Exception> _fail;
    private readonly Queue<(long Id, TaskCompletionSource Completion)> _batches = [];
    private long _nextBatch;

    internal AotWebViewRenderer(IServiceProvider services, ILoggerFactory logging, IComponentActivator activator,
        Dispatcher dispatcher, AotJsRuntime runtime, Action<string> send, Action<Exception> fail) : base(services, logging, activator)
    {
        _dispatcher = dispatcher;
        _send = send;
        _fail = fail;
        ElementReferenceContext = runtime.ElementReferenceContext;
    }

    public override Dispatcher Dispatcher => _dispatcher;

    internal Task AddRootAsync([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type componentType,
        string selector, ParameterView parameters)
    {
        var id = AssignRootComponentId(InstantiateComponent(componentType));
        _send(AotWire.Message("AttachToDocument", writer => { writer.WriteNumberValue(id); writer.WriteStringValue(selector); }));
        return RenderRootComponentAsync(id, parameters);
    }

    protected override Task UpdateDisplayAsync(in RenderBatch renderBatch)
    {
        // A browser must acknowledge each batch before its OnAfterRender callbacks.
        // Bound retained batches when a stalled document cannot consume rendering.
        if (_batches.Count >= 256) throw new InvalidOperationException("The Blazor document has too many unacknowledged render batches.");
        using var stream = new MemoryStream();
        using (var writer = new RenderBatchWriter(stream, leaveOpen: true)) writer.Write(renderBatch);
        var id = ++_nextBatch;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _batches.Enqueue((id, completion));
        var bytes = Convert.ToBase64String(stream.ToArray());
        _send(AotWire.Message("RenderBatch", writer => { writer.WriteNumberValue(id); writer.WriteStringValue(bytes); }));
        return completion.Task;
    }

    internal void Acknowledge(long id)
    {
        if (!_batches.TryPeek(out var next) || next.Id != id)
            throw new InvalidOperationException("Render acknowledgments must match the oldest pending batch.");
        _batches.Dequeue().Completion.TrySetResult();
    }

    internal void CancelPendingBatches()
    {
        while (_batches.TryDequeue(out var next)) next.Completion.TrySetCanceled();
    }

    protected override void HandleException(Exception exception) => _fail(exception);
}
