using Microsoft.Extensions.Logging;

namespace NetWiv;

/// <summary>Source-generated log messages for the IPC and command pipeline.</summary>
internal static partial class CoreLog
{
    internal const string IpcCategory = "NetWiv.Ipc";
    internal const string CommandCategory = "NetWiv.Commands";

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Discarded an IPC message from webview '{WebviewLabel}' because it could not be parsed.")]
    internal static partial void MalformedMessage(ILogger logger, string webviewLabel, Exception exception);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "Rejected an IPC request from webview '{WebviewLabel}': {Reason}")]
    internal static partial void RejectedRequest(ILogger logger, string? webviewLabel, string reason);

    [LoggerMessage(EventId = 3, Level = LogLevel.Debug,
        Message = "Command '{Command}' was denied by the ACL for window '{WindowLabel}', webview '{WebviewLabel}' and origin '{Origin}'.")]
    internal static partial void CommandDenied(ILogger logger, string command, string windowLabel, string webviewLabel, string origin);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error,
        Message = "Command '{Command}' failed with an unhandled exception.")]
    internal static partial void CommandFailed(ILogger logger, string command, Exception exception);
}
