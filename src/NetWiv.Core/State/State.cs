using System.Diagnostics.CodeAnalysis;

namespace NetWiv;

public readonly struct State<T> where T : notnull
{
    internal State(T value) => Value = value;
    public T Value { get; }
    public T Inner() => Value;
    public static implicit operator T(State<T> state) => state.Value;
}

internal sealed class StateStore : IDisposable
{
    private readonly Dictionary<Type, object> _values = [];
    private readonly object _gate = new();
    private bool _disposed;

    public bool Manage<T>(T value) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _values.TryAdd(typeof(T), value);
        }
    }

    public State<T> Get<T>() where T : notnull =>
        TryGet<T>() ?? throw new InvalidOperationException($"State '{typeof(T)}' is not managed.");

    public State<T>? TryGet<T>() where T : notnull
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _values.TryGetValue(typeof(T), out var value) ? new State<T>((T)value) : null;
        }
    }

    public bool Unmanage<T>([MaybeNullWhen(false)] out T value) where T : notnull
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_values.Remove(typeof(T), out var removed)) { value = (T)removed; return true; }
            value = default;
            return false;
        }
    }

    public void Dispose()
    {
        object[] values;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            values = _values.Values.Distinct(ReferenceEqualityComparer.Instance).ToArray();
            _values.Clear();
        }
        List<Exception>? errors = null;
        foreach (var value in values)
        {
            if (value is not IDisposable disposable) continue;
            try { disposable.Dispose(); }
            catch (Exception exception) { (errors ??= []).Add(exception); }
        }
        if (errors is not null) throw new AggregateException("One or more managed states failed to dispose.", errors);
    }
}
