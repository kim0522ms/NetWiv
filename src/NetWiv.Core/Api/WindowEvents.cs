using System.Text.Json;

namespace NetWiv;

public sealed record AppEvent<T>(long Id, string Name, string? Label, T Payload);

public sealed record ScaleFactorChanged(double ScaleFactor, PhysicalSize Size);

public enum DragDropEventType { Enter, Over, Drop, Leave }

public abstract record DragDropEvent(DragDropEventType Type);

public sealed record DragEnterEvent(IReadOnlyList<string> Paths, PhysicalPosition Position)
    : DragDropEvent(DragDropEventType.Enter);

public sealed record DragOverEvent(PhysicalPosition Position)
    : DragDropEvent(DragDropEventType.Over);

public sealed record DragDroppedEvent(IReadOnlyList<string> Paths, PhysicalPosition Position)
    : DragDropEvent(DragDropEventType.Drop);

public sealed record DragLeaveEvent() : DragDropEvent(DragDropEventType.Leave);

public partial class Window
{
    public IDisposable OnResized(Action<AppEvent<PhysicalSize>> handler) =>
        ListenTyped("tauri://resize", handler, ApiJson.ReadPhysicalSize);

    public IDisposable OnMoved(Action<AppEvent<PhysicalPosition>> handler) =>
        ListenTyped("tauri://move", handler, ApiJson.ReadPhysicalPosition);

    public IDisposable OnFocusChanged(Action<AppEvent<bool>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new CompositeSubscription(
            Listen("tauri://focus", value => handler(WindowEventPayload.Map(value, true))),
            Listen("tauri://blur", value => handler(WindowEventPayload.Map(value, false))));
    }

    public IDisposable OnScaleChanged(Action<AppEvent<ScaleFactorChanged>> handler) =>
        ListenTyped("tauri://scale-change", handler, WindowEventPayload.ReadScaleFactorChanged);

    public IDisposable OnThemeChanged(Action<AppEvent<Theme>> handler) =>
        ListenTyped("tauri://theme-changed", handler, ApiJson.ReadTheme);

    public IDisposable OnDragDropEvent(Action<AppEvent<DragDropEvent>> handler) =>
        WindowEventPayload.ListenForDragDrop(Listen, handler);

    private IDisposable ListenTyped<T>(
        string eventName,
        Action<AppEvent<T>> handler,
        Func<JsonElement, T> readPayload)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Listen(eventName, value => handler(WindowEventPayload.Map(value, readPayload(value.Payload))));
    }
}

public partial class Webview
{
    public IDisposable OnDragDropEvent(Action<AppEvent<DragDropEvent>> handler) =>
        WindowEventPayload.ListenForDragDrop(Listen, handler);
}

internal static class WindowEventPayload
{
    internal static AppEvent<T> Map<T>(AppEvent value, T payload) =>
        new(value.Id, value.Name, value.Label, payload);

    internal static ScaleFactorChanged ReadScaleFactorChanged(JsonElement value) =>
        new(value.GetProperty("scaleFactor").GetDouble(), ApiJson.ReadPhysicalSize(value.GetProperty("size")));

    internal static IDisposable ListenForDragDrop(
        Func<string, Action<AppEvent>, IDisposable> listen,
        Action<AppEvent<DragDropEvent>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new CompositeSubscription(
            listen("tauri://drag-enter", value => handler(Map(value, ReadDragWithPaths(value.Payload, true)))),
            listen("tauri://drag-over", value => handler(Map<DragDropEvent>(value,
                new DragOverEvent(ApiJson.ReadPhysicalPosition(value.Payload.GetProperty("position")))))),
            listen("tauri://drag-drop", value => handler(Map(value, ReadDragWithPaths(value.Payload, false)))),
            listen("tauri://drag-leave", value => handler(Map<DragDropEvent>(value, new DragLeaveEvent()))));
    }

    private static DragDropEvent ReadDragWithPaths(JsonElement value, bool enter)
    {
        var paths = value.GetProperty("paths").EnumerateArray()
            .Select(static path => path.GetString()
                ?? throw new JsonException("A drag-drop path was not a string."))
            .ToArray();
        var position = ApiJson.ReadPhysicalPosition(value.GetProperty("position"));
        return enter ? new DragEnterEvent(paths, position) : new DragDroppedEvent(paths, position);
    }
}

internal sealed class CompositeSubscription(params IDisposable[] subscriptions) : IDisposable
{
    private IDisposable[]? _subscriptions = subscriptions;

    public void Dispose()
    {
        var subscriptions = Interlocked.Exchange(ref _subscriptions, null);
        if (subscriptions is null) return;
        foreach (var subscription in subscriptions) subscription.Dispose();
    }
}
