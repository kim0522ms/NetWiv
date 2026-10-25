namespace NetWiv;

/// <summary>Represents an error reported while evaluating a JavaScript expression in a WebView.</summary>
/// <remarks>
/// Errors thrown or rejected by a valid expression retain their JavaScript name, message and stack when available.
/// Malformed syntax or engine failures reported without an evaluation acknowledgment use the name
/// <c>JavaScriptEvaluationError</c>, a generic message and no JavaScript stack trace; the native
/// runtime cannot reliably classify these failures as a specific JavaScript error.
/// </remarks>
public sealed class JavaScriptException : Exception
{
    public JavaScriptException(string name, string message, string? javaScriptStackTrace = null) : base(message)
    {
        Name = name;
        JavaScriptStackTrace = javaScriptStackTrace;
    }

    /// <summary>The JavaScript error name, or <c>JavaScriptEvaluationError</c> for an unclassified engine failure.</summary>
    public string Name { get; }

    /// <summary>The JavaScript stack trace, when the runtime provided one.</summary>
    public string? JavaScriptStackTrace { get; }
}
