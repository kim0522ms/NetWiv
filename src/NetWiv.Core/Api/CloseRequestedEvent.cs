namespace NetWiv;

public sealed class CloseRequestedEvent
{
    private int _preventDefault;
    internal CloseRequestedEvent(AppEvent value) { Id = value.Id; Name = value.Name; Label = value.Label; }
    public long Id { get; }
    public string Name { get; }
    public string? Label { get; }
    public void PreventDefault() => Interlocked.Exchange(ref _preventDefault, 1);
    public bool IsPreventDefault() => Volatile.Read(ref _preventDefault) != 0;
}

public partial class Window
{
    /// <summary>Waits for the handler before destroying the window unless PreventDefault was called.</summary>
    public IDisposable OnCloseRequested(Func<CloseRequestedEvent, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Listen("tauri://close-requested", value => App.RunEventHandler(value, async () =>
        {
            var request = new CloseRequestedEvent(value);
            await handler(request).ConfigureAwait(false);
            if (!request.IsPreventDefault()) await DestroyAsync().ConfigureAwait(false);
        }));
    }

    public IDisposable OnCloseRequested(Action<CloseRequestedEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return OnCloseRequested(value => { handler(value); return Task.CompletedTask; });
    }
}

public sealed partial class AppHandle
{
    /// <summary>Reports asynchronous event-handler or follow-up failures. Without a subscriber, failures are written to standard error.</summary>
    public event Action<AppEvent, Exception>? EventHandlerFailed;

    internal void RunEventHandler(AppEvent value, Func<Task> handler) => _ = ObserveEventHandler(value, handler);

    private async Task ObserveEventHandler(AppEvent value, Func<Task> handler)
    {
        try { await handler().ConfigureAwait(false); }
        catch (Exception error)
        {
            ReportEventHandlerFailure(value, error);
        }
    }

    private void ReportEventHandlerFailure(AppEvent value, Exception error)
    {
        try
        {
            if (EventHandlerFailed is { } report) report(value, error);
            else Console.Error.WriteLine($"NetWiv event '{value.Name}' for '{value.Label}' failed: {error}");
        }
        catch (Exception reportingError)
        {
            Console.Error.WriteLine($"NetWiv event error reporting failed: {reportingError}; original error: {error}");
        }
    }
}
