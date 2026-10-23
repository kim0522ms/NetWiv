using System.Text.Json;
using NetWiv.Runtime;

namespace NetWiv;

public sealed record MenuEvent(string Id);

/// <summary>A macOS request to reopen the running application.</summary>
/// <param name="HasVisibleWindows">The value reported by AppKit; minimized windows also count as visible.</param>
public sealed record ReopenEvent(bool HasVisibleWindows);

/// <summary>URLs or files opened by the operating system on macOS.</summary>
public sealed record OpenedEvent(IReadOnlyList<Uri> Urls);

public sealed partial class AppHandle
{
    private readonly object _nativeEventGate = new();
    private readonly List<Action<AppHandle, MenuEvent>> _menuEventHandlers = [];
    private readonly List<Action<AppHandle, TrayIconEvent>> _trayEventHandlers = [];
    private readonly List<Func<AppHandle, ReopenEvent, Task>> _reopenHandlers = [];
    private readonly List<Func<AppHandle, OpenedEvent, Task>> _openedHandlers = [];

    /// <summary>Handles macOS URL and file open requests, including startup activation. Other platforms do not raise this event.</summary>
    /// <remarks>Handlers start on the event-loop thread and must not block it. Async handlers may overlap;
    /// failures are reported through EventHandlerFailed. No window is restored automatically.
    /// Application-emitted events with the same name do not invoke these handlers.</remarks>
    public void OnOpened(Func<AppHandle, OpenedEvent, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_nativeEventGate) { ObjectDisposedException.ThrowIf(_disposed, this); _openedHandlers.Add(handler); }
    }

    /// <inheritdoc cref="OnOpened(Func{AppHandle, OpenedEvent, Task})"/>
    public void OnOpened(Action<AppHandle, OpenedEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        OnOpened((app, value) => { handler(app, value); return Task.CompletedTask; });
    }

    /// <summary>Handles macOS Dock/Finder reopen requests. Other platforms do not raise this event.</summary>
    /// <remarks>No window is restored automatically. Handlers start on the event-loop thread and must not
    /// block it. Async handlers may overlap; failures are reported through EventHandlerFailed.
    /// Application-emitted events with the same name do not invoke these handlers.</remarks>
    public void OnReopen(Func<AppHandle, ReopenEvent, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_nativeEventGate) { ObjectDisposedException.ThrowIf(_disposed, this); _reopenHandlers.Add(handler); }
    }

    /// <inheritdoc cref="OnReopen(Func{AppHandle, ReopenEvent, Task})"/>
    public void OnReopen(Action<AppHandle, ReopenEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        OnReopen((app, value) => { handler(app, value); return Task.CompletedTask; });
    }

    public void OnMenuEvent(Action<AppHandle, MenuEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_nativeEventGate) { ObjectDisposedException.ThrowIf(_disposed, this); _menuEventHandlers.Add(handler); }
    }

    public void OnTrayIconEvent(Action<AppHandle, TrayIconEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_nativeEventGate) { ObjectDisposedException.ThrowIf(_disposed, this); _trayEventHandlers.Add(handler); }
    }

    private void PublishNativeEvent(RuntimeEvent value)
    {
        if (_disposed) return;
        if (value.EventName == "netwiv://opened")
        {
            var opened = new OpenedEvent(Array.AsReadOnly(value.Payload.GetProperty("urls").EnumerateArray()
                .Select(url => new Uri(url.GetString()!, UriKind.Absolute)).ToArray()));
            Func<AppHandle, OpenedEvent, Task>[] handlers;
            lock (_nativeEventGate) handlers = _openedHandlers.ToArray();
            var notification = new AppEvent(0, value.EventName, null, value.Payload);
            foreach (var handler in handlers)
            {
                if (_disposed) return;
                RunEventHandler(notification, () => handler(this, opened));
            }
        }
        else if (value.EventName == "netwiv://reopen")
        {
            var reopen = new ReopenEvent(value.Payload.GetProperty("hasVisibleWindows").GetBoolean());
            Func<AppHandle, ReopenEvent, Task>[] handlers;
            lock (_nativeEventGate) handlers = _reopenHandlers.ToArray();
            var notification = new AppEvent(0, value.EventName, null, value.Payload);
            foreach (var handler in handlers)
            {
                if (_disposed) return;
                RunEventHandler(notification, () => handler(this, reopen));
            }
        }
        else if (value.EventName == "menu-action")
        {
            var menuEvent = new MenuEvent(value.Payload.GetProperty("id").GetString()!);
            Action<AppHandle, MenuEvent>[] handlers;
            lock (_nativeEventGate) handlers = _menuEventHandlers.ToArray();
            foreach (var handler in handlers) handler(this, menuEvent);
        }
        else if (value.EventName == "tray-action")
        {
            var trayEvent = ReadTrayEvent(value.Payload);
            Action<AppHandle, TrayIconEvent>[] handlers;
            lock (_nativeEventGate) handlers = _trayEventHandlers.ToArray();
            foreach (var handler in handlers) handler(this, trayEvent);
        }
    }

    private static TrayIconEvent ReadTrayEvent(JsonElement value)
    {
        var type = value.GetProperty("type").GetString() switch
        {
            "Click" => TrayIconEventType.Click, "DoubleClick" => TrayIconEventType.DoubleClick,
            "Enter" => TrayIconEventType.Enter, "Move" => TrayIconEventType.Move, "Leave" => TrayIconEventType.Leave,
            var invalid => throw new InvalidOperationException($"Unknown native tray event '{invalid}'.")
        };
        var rect = value.GetProperty("rect");
        MouseButton? button = value.TryGetProperty("button", out var buttonValue) ? buttonValue.GetString() switch
        {
            "Left" => MouseButton.Left, "Right" => MouseButton.Right, "Middle" => MouseButton.Middle,
            var invalid => throw new InvalidOperationException($"Unknown native mouse button '{invalid}'.")
        } : null;
        MouseButtonState? state = value.TryGetProperty("buttonState", out var stateValue) ? stateValue.GetString() switch
        {
            "Up" => MouseButtonState.Up, "Down" => MouseButtonState.Down,
            var invalid => throw new InvalidOperationException($"Unknown native mouse state '{invalid}'.")
        } : null;
        var size = rect.GetProperty("size");
        if (size.TryGetProperty("Physical", out var physicalSize)) size = physicalSize;
        return new(type, value.GetProperty("id").GetString()!, ReadPosition(value.GetProperty("position")),
            new(ReadPosition(rect.GetProperty("position")), ApiJson.ReadPhysicalSize(size)), button, state);
    }

    private static PhysicalPosition<double> ReadPosition(JsonElement position)
    {
        if (position.TryGetProperty("Physical", out var physical)) position = physical;
        return new(position.GetProperty("x").GetDouble(), position.GetProperty("y").GetDouble());
    }

    private void ClearNativeEventHandlers()
    {
        lock (_nativeEventGate) { _menuEventHandlers.Clear(); _trayEventHandlers.Clear(); _reopenHandlers.Clear(); _openedHandlers.Clear(); }
    }
}
