using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.JSInterop;

namespace NetWiv.BlazorHybrid;

// Managed WebView's wire completion always creates JSException. Use the pinned
// JSRuntime's own task completion helper to preserve JSDisconnectedException,
// including for module references and calls made during component disposal.
// This adapter is used only by the managed host, never the NativeAOT host.
internal static class ManagedJsCompletion
{
    private static readonly Action<object, Exception> SetException = Type
        .GetType("Microsoft.JSInterop.Infrastructure.TaskGenericsUtil, Microsoft.JSInterop", throwOnError: true)!
        .GetMethod("SetTaskCompletionSourceException", BindingFlags.Public | BindingFlags.Static)!
        .CreateDelegate<Action<object, Exception>>();

    internal static void Fail(JSRuntime runtime, long taskId, Exception error)
    {
        // Cancellation completes the task before removing it from _pendingTasks.
        // Unregister before claiming it; an already-running cancellation callback
        // owns completion. Do not wait for it (it may reenter page retirement).
        if (CancellationRegistrations(runtime).TryRemove(taskId, out var registration) && !registration.Unregister())
            return;
        if (!PendingTasks(runtime).TryRemove(taskId, out var completion)) return;
        SetException(completion, error);
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_pendingTasks")]
    private static extern ref ConcurrentDictionary<long, object> PendingTasks(JSRuntime runtime);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_cancellationRegistrations")]
    private static extern ref ConcurrentDictionary<long, CancellationTokenRegistration> CancellationRegistrations(JSRuntime runtime);
}
