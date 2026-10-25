using System.Text.Json;

namespace NetWiv;

public sealed partial class AppHandle
{
    private readonly Dictionary<CancellationTokenSource, (string Webview, ulong Document)> _scriptEvaluations = [];

    /// <summary>Evaluates a JavaScript expression in the current document of the specified WebView and returns its JSON value.</summary>
    /// <param name="webviewLabel">The label of the WebView whose current document will evaluate the expression.</param>
    /// <param name="script">An expression, or an immediately invoked function expression for multiple statements.</param>
    /// <param name="cancellationToken">A token that cancels the .NET operation when requested or when the document ends.</param>
    /// <exception cref="InvalidOperationException">The WebView has not committed a document or its document is closed.</exception>
    /// <exception cref="JavaScriptException">The evaluated JavaScript throws an exception.</exception>
    /// <exception cref="NotSupportedException">The desktop runtime does not support asynchronous script evaluation.</exception>
    public async Task<JsonElement> EvaluateScriptAsync(
        string webviewLabel,
        string script,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(webviewLabel);
        ArgumentNullException.ThrowIfNull(script);
        cancellationToken.ThrowIfCancellationRequested();

        CancellationTokenSource lifetime;
        ulong documentId;
        lock (_invocationGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_invocationsClosed) throw new InvalidOperationException("The application is shutting down and no longer accepts script evaluations.");
            if (!_documents.TryGetValue(webviewLabel, out var document) || document.Closed || document.DocumentId == 0)
                throw new InvalidOperationException($"WebView '{webviewLabel}' has no current document to evaluate.");
            documentId = document.DocumentId;
            lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _scriptEvaluations.Add(lifetime, (webviewLabel, documentId));
        }

        Task<JsonElement>? evaluation = null;
        try
        {
            evaluation = _runtime.EvaluateScriptAsync(webviewLabel, script, documentId, lifetime.Token);
            var result = await evaluation.WaitAsync(lifetime.Token).ConfigureAwait(false);
            lock (_invocationGate)
                if (!IsCurrentDocument(webviewLabel, documentId))
                    throw new OperationCanceledException("The document was replaced during script evaluation.", lifetime.Token);
            return result.Clone();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            if (evaluation is not null) _ = ObserveEvaluationAsync(evaluation);
            throw new OperationCanceledException(cancellationToken.IsCancellationRequested ? cancellationToken : lifetime.Token);
        }
        finally
        {
            lock (_invocationGate) _scriptEvaluations.Remove(lifetime);
            lifetime.Dispose();
        }
    }

    private static async Task ObserveEvaluationAsync(Task<JsonElement> evaluation)
    {
        try { await evaluation.ConfigureAwait(false); }
        catch { }
    }

    private void CancelScriptEvaluations(string? webviewLabel = null, ulong documentId = 0, bool closeAdmission = false)
    {
        CancellationTokenSource[] pending;
        lock (_invocationGate)
        {
            if (closeAdmission) _invocationsClosed = true;
            pending = _scriptEvaluations.Where(entry => webviewLabel is null ||
                entry.Value.Webview == webviewLabel && (documentId == 0 || entry.Value.Document != documentId))
                .Select(entry => entry.Key).ToArray();
        }
        foreach (var lifetime in pending) _ = CancelScriptEvaluation(lifetime);
    }

    private static async Task CancelScriptEvaluation(CancellationTokenSource lifetime)
    {
        try { await lifetime.CancelAsync().ConfigureAwait(false); }
        catch (ObjectDisposedException) { }
        catch (Exception error) { Console.Error.WriteLine($"NetWiv script evaluation cancellation callback failed: {error}"); }
    }
}
