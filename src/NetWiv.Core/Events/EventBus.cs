using System.Text.Json;
using NetWiv.Runtime;

namespace NetWiv;

public sealed record AppEvent(long Id, string Name, string? Label, JsonElement Payload);

public sealed class EventBus : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Dictionary<long, Listener>> _listeners = new(StringComparer.Ordinal);
    private long _nextId;
    private volatile bool _disposed;

    public IDisposable Listen(string eventName, Action<AppEvent> handler) => Listen(eventName, RuntimeEventTarget.Any, handler);

    public IDisposable Listen(string eventName, RuntimeEventTarget target, Action<AppEvent> handler) =>
        Register(eventName, target, handler, false);

    public IDisposable Once(string eventName, Action<AppEvent> handler) => Once(eventName, RuntimeEventTarget.Any, handler);

    public IDisposable Once(string eventName, RuntimeEventTarget target, Action<AppEvent> handler) =>
        Register(eventName, target, handler, true);

    private IDisposable Register(string eventName, RuntimeEventTarget target, Action<AppEvent> handler, bool once)
    {
        EventName.Validate(eventName);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(handler);
        var id = Interlocked.Increment(ref _nextId);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_listeners.TryGetValue(eventName, out var listeners))
                _listeners[eventName] = listeners = [];
            listeners.Add(id, new(target, handler, once));
        }
        return new Subscription(this, eventName, id);
    }

    internal bool HasListener(string eventName, Func<RuntimeEventTarget, bool> matches)
    {
        lock (_gate) return _listeners.TryGetValue(eventName, out var listeners) && listeners.Values.Any(listener => matches(listener.Target));
    }

    internal void Publish(string eventName, RuntimeEventTarget target, JsonElement payload, Func<RuntimeEventTarget, bool> matches)
    {
        KeyValuePair<long, Listener>[] listeners;
        lock (_gate)
        {
            listeners = _listeners.TryGetValue(eventName, out var values) ? [.. values] : [];
        }
        foreach (var (id, listener) in listeners)
        {
            if (_disposed) return;
            if (!matches(listener.Target)) continue;
            if (listener.Once)
            {
                if (Interlocked.Exchange(ref listener.Fired, 1) != 0) continue;
                Remove(eventName, id);
            }
            listener.Handler(new AppEvent(id, eventName, target.Label, payload.Clone()));
        }
    }

    private void Remove(string eventName, long id)
    {
        lock (_gate)
        {
            if (_listeners.TryGetValue(eventName, out var listeners)) listeners.Remove(id);
        }
    }

    /// <summary>Removes the backend listener identified by AppEvent.Id; unknown IDs are ignored.</summary>
    public void Unlisten(long id)
    {
        lock (_gate)
            foreach (var listeners in _listeners.Values)
                if (listeners.Remove(id)) break;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _listeners.Clear();
        }
    }

    private sealed class Subscription(EventBus owner, string eventName, long id) : IDisposable
    {
        private EventBus? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Remove(eventName, id);
    }

    private sealed class Listener(RuntimeEventTarget target, Action<AppEvent> handler, bool once)
    {
        internal RuntimeEventTarget Target { get; } = target;
        internal Action<AppEvent> Handler { get; } = handler;
        internal bool Once { get; } = once;
        internal int Fired;
    }
}
