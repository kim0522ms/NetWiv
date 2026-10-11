using Microsoft.Extensions.DependencyInjection;
using NetWiv;
using Xunit;

namespace NetWiv.Generators.Tests;

public sealed class ServiceInjectionTests
{
    [Fact]
    public async Task EachInvocationResolvesFromItsOwnDisposedScope()
    {
        await using var services = new ServiceCollection()
            .AddSingleton<ProbeLog>()
            .AddScoped<ScopedProbe>()
            .BuildServiceProvider();
        using var app = new Builder().UseRuntime(new RecordingRuntime()).UseServiceProvider(services)
            .InvokeHandler("scoped_probe").Build();

        var first = await app.Handle.InvokeAsync("scoped_probe", JsonValue.EmptyObject);
        var second = await app.Handle.InvokeAsync("scoped_probe", JsonValue.EmptyObject);

        Assert.NotEqual(first.GetInt32(), second.GetInt32());
        Assert.Equal(2, services.GetRequiredService<ProbeLog>().Disposed);
    }

    [Fact]
    public async Task ServiceParametersMixWithJsonArgumentsKeysAndOptionalServices()
    {
        await using var services = new ServiceCollection()
            .AddKeyedSingleton("primary", new Greeter("Hello"))
            .AddKeyedSingleton(7, new Greeter("Seven"))
            .AddKeyedSingleton(ServiceSlot.Backup, new Greeter("Backup"))
            .BuildServiceProvider();
        using var app = new Builder().UseRuntime(new RecordingRuntime()).UseServiceProvider(services)
            .InvokeHandler("greet_with_services").Build();

        var result = await app.Handle.InvokeAsync("greet_with_services", JsonValue.Parse("""{"name":"Ada"}"""));

        Assert.Equal("Hello Ada|Seven|Backup|absent", result.GetString());
    }

    [Fact]
    public async Task MissingRequiredServiceFailsOnlyTheInvocation()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        using var configured = new Builder().UseRuntime(new RecordingRuntime()).UseServiceProvider(services)
            .InvokeHandler("scoped_probe").Build();
        using var unconfigured = new Builder().UseRuntime(new RecordingRuntime()).InvokeHandler("scoped_probe").Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            configured.Handle.InvokeAsync("scoped_probe", JsonValue.EmptyObject).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            unconfigured.Handle.InvokeAsync("scoped_probe", JsonValue.EmptyObject).AsTask());
        Assert.Null(unconfigured.Handle.Services.GetService(typeof(ProbeLog)));
    }
}

public sealed class ProbeLog
{
    private int _next;
    public int Disposed;
    public int Next() => Interlocked.Increment(ref _next);
}

public sealed class ScopedProbe(ProbeLog log) : IDisposable
{
    public int Id { get; } = log.Next();
    public void Dispose() => Interlocked.Increment(ref log.Disposed);
}

public sealed record Greeter(string Greeting);

public interface IUnregisteredService;

public enum ServiceSlot
{
    Primary,
    Backup,
}

public static class ServiceCommands
{
    [Command("scoped_probe")]
    public static int ScopedIdentity([FromServices] ScopedProbe first, [FromServices] ScopedProbe second) =>
        ReferenceEquals(first, second) ? first.Id : throw new InvalidOperationException("One invocation must share its scope.");

    [Command]
    public static string GreetWithServices(
        string name,
        [FromKeyedServices("primary")] Greeter primary,
        [FromKeyedServices(7)] Greeter numbered,
        [FromKeyedServices(ServiceSlot.Backup)] Greeter backup,
        [FromServices] IUnregisteredService? missing) =>
        $"{primary.Greeting} {name}|{numbered.Greeting}|{backup.Greeting}|{(missing is null ? "absent" : "present")}";
}
