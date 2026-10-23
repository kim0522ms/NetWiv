namespace NetWiv.BlazorHybrid.Managed.Fixture;

public sealed class ManagedFixtureScope : IAsyncDisposable
{
    public bool Disposed { get; private set; }

    public async ValueTask DisposeAsync()
    {
        await Task.Yield();
        Disposed = true;
    }
}

public static class ManagedFixtureProofState
{
    private static readonly object Gate = new();
    private static readonly List<ManagedFixtureScope> Scopes = [];
    private static int _componentsDisposed;
    private static int _moduleDisposalOutcomes;

    public static void ComponentCreated(ManagedFixtureScope scope)
    {
        lock (Gate) if (!Scopes.Contains(scope)) Scopes.Add(scope);
    }

    public static void ComponentDisposed() => Interlocked.Increment(ref _componentsDisposed);

    public static void ModuleDisposalFinished() => Interlocked.Increment(ref _moduleDisposalOutcomes);

    public static void AssertCleaned(int expected)
    {
        lock (Gate)
        {
            if (Scopes.Count != expected || Scopes.Any(scope => !scope.Disposed) ||
                Volatile.Read(ref _componentsDisposed) != expected ||
                Volatile.Read(ref _moduleDisposalOutcomes) != expected)
                throw new InvalidOperationException("RCL component, module, or page-scope cleanup was incomplete.");
        }
    }
}
