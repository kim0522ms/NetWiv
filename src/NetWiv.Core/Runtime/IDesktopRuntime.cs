using System.Text.Json;

namespace NetWiv.Runtime;

/// <summary>The platform boundary used by the platform-neutral application model.</summary>
public interface IDesktopRuntime
{
    void Run(RuntimeContext context);
    void Dispatch(Action action);
    /// <summary>Resolves OS directories; the default preserves the legacy adapter behavior during migration.</summary>
    Task<string> ResolveDirectoryAsync(int directory, string? path, string identifier, CancellationToken cancellationToken = default) =>
        Task.FromResult(path is null ? BuiltInCommands.ResolveDirectory(directory, identifier) :
            Path.Combine(BuiltInCommands.ResolveDirectory(directory, identifier), path));
    Task<JsonElement> InvokeAsync(
        string command,
        JsonElement args,
        CancellationToken cancellationToken = default);
    Task<JsonElement> InvokeAsync(
        string command,
        JsonElement args,
        RuntimeCommandContext context,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(command, args, cancellationToken);
    async Task<RuntimeInvokeResponse> InvokeResponseAsync(
        string command,
        JsonElement args,
        RuntimeCommandContext context,
        CancellationToken cancellationToken = default) =>
        RuntimeInvokeResponse.FromJson(await InvokeAsync(command, args, context, cancellationToken));
    void EvaluateScript(string webviewLabel, string script);
    void EvaluateScript(string webviewLabel, string script, ulong documentId) => EvaluateScript(webviewLabel, script);
    Task<JsonElement> EvaluateScriptAsync(
        string webviewLabel,
        string script,
        ulong documentId,
        CancellationToken cancellationToken = default) =>
        Task.FromException<JsonElement>(new NotSupportedException("This desktop runtime does not support asynchronous script evaluation."));
    void Exit(int exitCode = 0);
}
