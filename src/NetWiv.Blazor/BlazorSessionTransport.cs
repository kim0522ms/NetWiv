using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;

namespace NetWiv.Blazor;

// The lifecycle adapter is intentionally pinned to Microsoft's WebView 8.0.32
// transport. Rendering and JS completion still run through Microsoft's runtime.
internal sealed class BlazorSessionTransport(Action<string> send)
{
    internal const int MaximumTrackedJsCalls = 4096;
    private const string Prefix = "__bwv:";
    private readonly object _gate = new();
    private readonly HashSet<long> _pendingJs = [];
    private readonly string _disconnectedMessage = "NetWiv Blazor document disconnected: " + Guid.NewGuid().ToString("N");
    private BlazorWebViewManager? _manager;
    private JSRuntime? _jsRuntime;
    private bool _retired;

    internal void Attach(BlazorWebViewManager manager) => _manager = manager;

    internal Task SendAsync(string message)
    {
        var taskId = BeginInvokeId(message);
        bool retired;
        bool overflow = false;
        lock (_gate)
        {
            retired = _retired;
            if (!retired && taskId is > 0 && !_pendingJs.Contains(taskId.Value))
            {
                overflow = _pendingJs.Count >= MaximumTrackedJsCalls;
                if (!overflow) _pendingJs.Add(taskId.Value);
            }
        }
        if (overflow) return RejectOverflowAsync(taskId!.Value);
        if (retired)
            return taskId is > 0 ? CompleteDisconnectedAsync(taskId.Value) : Task.CompletedTask;
        send(message);
        return Task.CompletedTask;
    }

    internal void Received(string message)
    {
        // IpcReceiver uses the nested result JSON, not the outer task ID.
        // Only remove a fully formed completion that the upstream parser admits.
        if (!TryRead(message, "EndInvokeJS", out var document)) return;
        using (document)
        {
            var root = document!.RootElement;
            if (root.GetArrayLength() < 4 || root[3].ValueKind != JsonValueKind.String) return;
            try
            {
                using var result = JsonDocument.Parse(root[3].GetString()!);
                var values = result.RootElement;
                if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() != 3 ||
                    values[0].ValueKind != JsonValueKind.Number || !values[0].TryGetInt64(out var taskId) ||
                    values[1].ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                    values[1].ValueKind == JsonValueKind.False && values[2].ValueKind != JsonValueKind.String) return;
                lock (_gate) _pendingJs.Remove(taskId);
            }
            catch (JsonException) { } // The real receiver reports malformed active-page messages.
        }
    }

    internal void Retire()
    {
        lock (_gate) _retired = true;
    }

    internal async Task DisconnectAsync()
    {
        await _manager!.TryDispatchAsync(services =>
            _jsRuntime = (JSRuntime)services.GetRequiredService<IJSRuntime>());
        long[] pending;
        lock (_gate)
        {
            _retired = true;
            pending = _pendingJs.ToArray();
            _pendingJs.Clear();
        }
        foreach (var taskId in pending) await CompleteDisconnectedAsync(taskId);
    }

    internal bool IsExpectedDisconnect(Exception error)
    {
        lock (_gate)
        {
            if (!_retired) return false;
        }
        if (error is AggregateException aggregate)
        {
            var errors = aggregate.Flatten().InnerExceptions;
            return errors.Count > 0 && errors.All(IsExpectedDisconnect);
        }
        return error is JSException && error.Message == _disconnectedMessage;
    }

    private async Task CompleteDisconnectedAsync(long taskId)
    {
        if (_jsRuntime is null)
            await _manager!.TryDispatchAsync(services => _jsRuntime = (JSRuntime)services.GetRequiredService<IJSRuntime>());
        if (_jsRuntime is null)
        {
            // PageContext publishes its scope after its constructor returns.
            // DisconnectAsync will complete calls emitted during construction.
            lock (_gate)
            {
                if (!_pendingJs.Contains(taskId) && _pendingJs.Count >= MaximumTrackedJsCalls)
                    throw TrackingLimit();
                _pendingJs.Add(taskId);
            }
            return;
        }
        // Keep the runtime reference, not its disposed service provider: callers
        // can still hold old IJSObjectReference/IJSRuntime instances after reload.
        DotNetDispatcher.EndInvokeJS(_jsRuntime!,
            JsonSerializer.Serialize(new object[] { taskId, false, _disconnectedMessage }));
    }

    private async Task RejectOverflowAsync(long taskId)
    {
        // Reject the actual JSRuntime task too, without retaining its ID. The
        // admitted calls remain available for retirement and normal completion.
        await CompleteDisconnectedAsync(taskId);
        throw TrackingLimit();
    }

    private static InvalidOperationException TrackingLimit() =>
        new("The Blazor document exceeded the limit of 4096 pending JavaScript calls.");

    private static long? BeginInvokeId(string message)
    {
        if (!TryRead(message, "BeginInvokeJS", out var document)) return null;
        using (document)
        {
            var root = document!.RootElement;
            return root.GetArrayLength() > 1 && root[1].ValueKind == JsonValueKind.Number && root[1].TryGetInt64(out var taskId) ? taskId : null;
        }
    }

    private static bool TryRead(string message, string type, out JsonDocument? document)
    {
        document = null;
        if (!message.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        try
        {
            document = JsonDocument.Parse(message.AsMemory(Prefix.Length));
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0 &&
                root[0].ValueKind == JsonValueKind.String && root[0].GetString() == type) return true;
        }
        catch (JsonException) { }
        document?.Dispose();
        document = null;
        return false;
    }
}
