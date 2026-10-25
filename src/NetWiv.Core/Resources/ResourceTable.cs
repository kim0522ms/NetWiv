namespace NetWiv;

public sealed class ResourceTable : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<uint, object> _resources = [];
    private readonly Dictionary<uint, (object Owner, ulong Document)> _owners = [];
    private uint _nextId = int.MaxValue;
    private bool _disposed;

    public uint Add<T>(T resource) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(resource);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_nextId == uint.MaxValue) throw new InvalidOperationException("The managed resource identifier range is exhausted.");
            var id = ++_nextId;
            _resources.Add(id, resource);
            return id;
        }
    }

    public T Get<T>(uint id) where T : notnull
    {
        lock (_gate)
            return _resources.TryGetValue(id, out var resource) && resource is T typed
                ? typed
                : throw new KeyNotFoundException($"The resource id {id} is invalid.");
    }

    internal uint AddOwned<T>(T resource, object owner, ulong document) where T : notnull
    {
        lock (_gate)
        {
            var id = Add(resource);
            _owners.Add(id, (owner, document));
            return id;
        }
    }

    internal void CloseDocument(Runtime.RuntimeDocumentChange change)
    {
        object[] stale;
        lock (_gate)
        {
            var ids = _owners.Where(entry => Equals(entry.Value.Owner, change.WebviewLabel) &&
                (change.Closed || entry.Value.Document != change.DocumentId)).Select(entry => entry.Key).ToArray();
            stale = ids.Select(id => _resources[id]).Distinct(ReferenceEqualityComparer.Instance).ToArray();
            foreach (var id in ids) { _owners.Remove(id); _resources.Remove(id); }
        }
        foreach (var resource in stale)
        {
            try { if (resource is IDisposable disposable) disposable.Dispose(); }
            catch (Exception error) { Console.Error.WriteLine($"NetWiv document resource cleanup failed: {error}"); }
        }
    }

    public object? TryGet(uint id)
    {
        lock (_gate) return _resources.GetValueOrDefault(id);
    }

    public bool Close(uint id) => Close(id, null, 0);

    internal bool Close(uint id, object? owner, ulong documentId)
    {
        object resource;
        lock (_gate)
        {
            if (owner is not null && _owners.TryGetValue(id, out var expected) &&
                (!Equals(expected.Owner, owner) || expected.Document != documentId))
                throw new InvokeException(owner is string ? "Resource belongs to another document." : "Resource belongs to another invocation owner.");
            if (!_resources.Remove(id, out resource!)) return false;
            _owners.Remove(id);
        }
        if (resource is IDisposable disposable) disposable.Dispose();
        return true;
    }

    public void Dispose()
    {
        object[] resources;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            resources = [.. _resources.Values];
            _resources.Clear();
            _owners.Clear();
        }
        List<Exception>? errors = null;
        foreach (var resource in resources)
        {
            if (resource is not IDisposable disposable) continue;
            try { disposable.Dispose(); }
            catch (Exception exception) { (errors ??= []).Add(exception); }
        }
        if (errors is not null) throw new AggregateException("One or more resources failed to dispose.", errors);
    }
}
