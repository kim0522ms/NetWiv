using System.Text.Json;
using NetWiv.Runtime;

namespace NetWiv;

public sealed record MenuEvent(string Id);

public sealed partial class AppHandle
{
    private readonly object _nativeEventGate = new();
    private readonly List<Action<AppHandle, MenuEvent>> _menuEventHandlers = [];
    private readonly List<Action<AppHandle, TrayIconEvent>> _trayEventHandlers = [];

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
        if (value.EventName == "menu-action")
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
        lock (_nativeEventGate) { _menuEventHandlers.Clear(); _trayEventHandlers.Clear(); }
    }
}
