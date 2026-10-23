namespace NetWiv;

/// <summary>A cooperative application exit request. It does not intercept OS shutdown or force exit.</summary>
public sealed class ExitRequestedEvent
{
    private int _preventDefault;
    internal ExitRequestedEvent(int exitCode, CancellationToken cancellationToken)
    {
        ExitCode = exitCode;
        CancellationToken = cancellationToken;
    }
    public int ExitCode { get; }
    public CancellationToken CancellationToken { get; }
    public void PreventDefault() => Interlocked.Exchange(ref _preventDefault, 1);
    public bool IsPreventDefault() => Volatile.Read(ref _preventDefault) != 0;
}

public sealed partial class AppHandle
{
    private readonly List<Func<AppHandle, ExitRequestedEvent, Task>> _exitRequestedHandlers = [];
    private bool _exitRequestPending;
    private long _exitRequestGeneration;

    /// <summary>Handles cooperative requests made through RequestExitAsync, in registration order.</summary>
    /// <remarks>A veto stops later handlers. Handlers have no UI-thread affinity; use asynchronous window
    /// operations or Dispatch. Force Exit, Restart, automatic last-window exit and OS shutdown bypass this hook.</remarks>
    public void OnExitRequested(Func<AppHandle, ExitRequestedEvent, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_nativeEventGate) { ObjectDisposedException.ThrowIf(_disposed, this); _exitRequestedHandlers.Add(handler); }
    }

    /// <inheritdoc cref="OnExitRequested(Func{AppHandle, ExitRequestedEvent, Task})"/>
    public void OnExitRequested(Action<AppHandle, ExitRequestedEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        OnExitRequested((app, value) => { handler(app, value); return Task.CompletedTask; });
    }

    /// <summary>Awaits application save/confirmation handlers, then exits unless vetoed or invalidated.</summary>
    /// <returns>True when exit was submitted; false after a veto, force exit or disposal.</returns>
    /// <remarks>Concurrent and reentrant requests are rejected. Each request snapshots its handlers;
    /// new registrations apply to the next request. Caller cancellation throws and prevents a later exit,
    /// even if a handler ignores its token. Handler failures are reported through EventHandlerFailed and
    /// propagated to the caller. Already started handlers may finish after cancellation or disposal.
    /// Cancellation and invalidation are checked before approval; they do not retract an approved native exit.
    /// This is an application request, not OS session-end or force-quit interception.</remarks>
    public async Task<bool> RequestExitAsync(int exitCode = 0, CancellationToken cancellationToken = default)
    {
        Func<AppHandle, ExitRequestedEvent, Task>[] handlers;
        long generation;
        lock (_nativeEventGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (_exitRequestPending) throw new InvalidOperationException("An application exit request is already pending.");
            _exitRequestPending = true;
            generation = _exitRequestGeneration;
            handlers = _exitRequestedHandlers.ToArray();
        }
        var request = new ExitRequestedEvent(exitCode, cancellationToken);
        var notification = new AppEvent(0, "netwiv://exit-requested", null, JsonValue.Null);
        try
        {
            foreach (var handler in handlers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (_nativeEventGate)
                    if (_disposed || generation != _exitRequestGeneration) return false;
                Task pending;
                try { pending = handler(this, request); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception error) { ReportEventHandlerFailure(notification, error); throw; }
                try { await pending.WaitAsync(cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Observe work that ignores caller cancellation, including any later failure.
                    RunEventHandler(notification, async () =>
                    {
                        try { await pending.ConfigureAwait(false); }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
                    });
                    throw;
                }
                catch (Exception error) { ReportEventHandlerFailure(notification, error); throw; }
                cancellationToken.ThrowIfCancellationRequested();
                if (request.IsPreventDefault()) return false;
            }
            lock (_nativeEventGate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_disposed || generation != _exitRequestGeneration) return false;
            }
            // Do not hold the registration gate while entering an adapter's own runtime lock.
            _runtime.Exit(exitCode);
            return true;
        }
        finally { lock (_nativeEventGate) _exitRequestPending = false; }
    }
}
