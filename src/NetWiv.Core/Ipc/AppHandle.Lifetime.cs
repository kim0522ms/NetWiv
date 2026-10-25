using NetWiv.Runtime;

namespace NetWiv;

public sealed partial class AppHandle
{
    private readonly object _invocationGate = new();
    private readonly Dictionary<CancellationTokenSource, (string? Webview, ulong Document)> _invocations = [];
    private TaskCompletionSource? _invocationsDrained;
    private bool _invocationsClosed;

    private CancellationTokenSource BeginInvocation(string webview, ulong document, bool trusted, CancellationToken caller)
    {
        lock (_invocationGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_invocationsClosed) throw new InvokeException("The application is shutting down and no longer accepts commands.");
            if (!trusted && !IsCurrentDocument(webview, document)) throw new InvokeException("The originating document has been replaced.");
            var lifetime = CancellationTokenSource.CreateLinkedTokenSource(caller);
            _invocations.Add(lifetime, (trusted ? null : webview, document));
            return lifetime;
        }
    }

    private void EndInvocation(CancellationTokenSource lifetime)
    {
        TaskCompletionSource? drained = null;
        lock (_invocationGate)
        {
            _invocations.Remove(lifetime);
            if (_invocations.Count == 0) (drained, _invocationsDrained) = (_invocationsDrained, null);
        }
        lifetime.Dispose();
        drained?.TrySetResult();
    }

    /// <summary>
    /// Requests cooperative cancellation of every command invocation in flight. With <paramref name="closeAdmission"/>,
    /// new invocations are rejected from the same atomic point, so a following wait observes every admitted command.
    /// </summary>
    internal void CancelInvocations(bool closeAdmission = false)
    {
        CancellationTokenSource[] pending;
        lock (_invocationGate)
        {
            _invocationsClosed |= closeAdmission;
            pending = _invocations.Keys.ToArray();
        }
        foreach (var lifetime in pending) _ = CancelInvocation(lifetime);
        CancelScriptEvaluations(closeAdmission: closeAdmission);
    }

    /// <summary>
    /// Waits until no command invocation, including its service scope disposal, is in flight.
    /// Returns false when <paramref name="cancellationToken"/> ends the wait first.
    /// </summary>
    internal async Task<bool> WaitForInvocationsAsync(CancellationToken cancellationToken)
    {
        Task drained;
        lock (_invocationGate)
        {
            if (_invocations.Count == 0) return true;
            drained = (_invocationsDrained ??= new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }
        try
        {
            await drained.WaitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private void ChangeDocument(RuntimeDocumentChange change)
    {
        CancellationTokenSource[] stale;
        lock (_invocationGate)
        {
            _documents[change.WebviewLabel] = change;
            stale = _invocations.Where(entry => entry.Value.Webview == change.WebviewLabel &&
                (change.Closed || entry.Value.Document != change.DocumentId)).Select(entry => entry.Key).ToArray();
        }
        foreach (var lifetime in stale) _ = CancelInvocation(lifetime);
        CancelScriptEvaluations(change.WebviewLabel, change.Closed ? 0 : change.DocumentId);
        Resources.CloseDocument(change);
    }

    private static async Task CancelInvocation(CancellationTokenSource lifetime)
    {
        try { await lifetime.CancelAsync().ConfigureAwait(false); }
        catch (ObjectDisposedException) { } // The invocation completed concurrently.
        catch (Exception error) { Console.Error.WriteLine($"NetWiv cancellation callback failed: {error}"); }
    }

    internal uint AddInvocationResource<T>(InvokeContext context, T resource) where T : notnull
    {
        lock (_invocationGate)
        {
            if (_disposed || context.InvocationCompleted || !context.IsBackend && !IsCurrentDocument(context.WebviewLabel, context.DocumentId))
            {
                if (resource is IDisposable disposable) disposable.Dispose();
                throw new InvokeException("The originating document has been replaced or the application stopped.");
            }
            var id = context.IsTrusted ? Resources.Add(resource) : Resources.AddOwned(resource, context.ResourceOwner, context.DocumentId);
            context.InvocationResources?.Add(id);
            return id;
        }
    }
}
