namespace NetWiv.BlazorHybrid.Aot;

/// <summary>Creates a document-owned object reference for explicitly registered NativeAOT JS callbacks.</summary>
public static class BlazorAotObjectReference
{
    public static BlazorAotObjectReference<T> Create<T>(T value) where T : class => BlazorAotObjectReference<T>.Create(value);
}

/// <summary>A NativeAOT callback reference. Create a new reference after its document closes.</summary>
public sealed class BlazorAotObjectReference<T> : IDisposable, IAotObjectReference where T : class
{
    private readonly object _gate = new();
    private T? _value;
    private IAotReferenceOwner? _owner;
    private long _id;
    private bool _disposed;

    private BlazorAotObjectReference(T value) => _value = value;
    public static BlazorAotObjectReference<T> Create(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(value);
    }

    public T Value
    {
        get
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _value!;
            }
        }
    }

    Type IAotObjectReference.TargetType => typeof(T);
    object IAotObjectReference.Value => Value;

    long IAotObjectReference.Bind(IAotReferenceOwner owner)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_owner is not null && !ReferenceEquals(_owner, owner))
                throw new InvalidOperationException("A callback reference belongs to one WebView document. Create a new reference for another document.");
            if (_owner is null)
            {
                var id = owner.Track(this);
                _owner = owner;
                _id = id;
            }
            return _id;
        }
    }

    public void Dispose()
    {
        IAotReferenceOwner? owner;
        long id;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _value = null;
            owner = _owner;
            id = _id;
        }
        owner?.Release(id);
    }
}

internal interface IAotObjectReference : IDisposable
{
    Type TargetType { get; }
    object Value { get; }
    long Bind(IAotReferenceOwner owner);
}

internal interface IAotReferenceOwner
{
    long Track(IAotObjectReference reference);
    void Release(long id);
}
