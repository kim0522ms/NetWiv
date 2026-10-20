using NetWiv;
using NetWiv.BlazorHybrid.Contracts;

namespace NetWiv.BlazorHybrid.Desktop;

public sealed class GreetingSequence
{
    private int _count;
    public int Next() => Interlocked.Increment(ref _count);
}

public sealed class GreetingService : IDisposable
{
    public bool IsDisposed { get; private set; }

    public string CreateGreeting(string name)
    {
        var normalizedName = name.Trim();
        return $"Hello, {normalizedName}!";
    }

    public void Dispose()
    {
        IsDisposed = true;
    }
}

public sealed class GreetingCommands(GreetingService service, GreetingSequence sequence)
{
    [Command]
    public Greeting Greet(string name)
    {
        var message = service.CreateGreeting(name);
        var count = sequence.Next();
        return new Greeting(message, count);
    }
}
