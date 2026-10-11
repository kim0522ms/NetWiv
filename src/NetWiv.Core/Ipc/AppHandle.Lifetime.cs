using NetWiv.Runtime;

namespace NetWiv;

public sealed partial class AppHandle
{
    private readonly object _invocationGate = new();
    private readonly Dictionary<CancellationTokenSource, (string? Webview, ulong Document)> _invocations = [];

    private CancellationTokenSource BeginInvocation(string webview, ulong document, bool trusted, CancellationToken caller)
    {
        lock (_invocationGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!trusted && !IsCurrentDocument(webview, document)) throw new InvokeException("The originating document has been replaced.");
            var lifetime = CancellationTokenSource.CreateLinkedTokenSource(caller);
            _invocations.Add(lifetime, (trusted ? null : webview, document));
            return lifetime;
        }
    }

    private void EndInvocation(CancellationTokenSource lifetime)
    {
        lock (_invocationGate) _invocations.Remove(lifetime);
        lifetime.Dispose();
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
            if (_disposed || !context.IsTrusted && !IsCurrentDocument(context.WebviewLabel, context.DocumentId))
            {
                if (resource is IDisposable disposable) disposable.Dispose();
                throw new InvokeException("The originating document has been replaced or the application stopped.");
            }
            return context.IsTrusted ? Resources.Add(resource) : Resources.AddOwned(resource, context.WebviewLabel, context.DocumentId);
        }
    }
}
