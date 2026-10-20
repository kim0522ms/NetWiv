using Microsoft.Extensions.DependencyInjection;
using NetWiv.Generated;

namespace NetWiv.BlazorHybrid.Shared;

// Hybrid recreates scoped services when a page attaches after navigation or reload.
// WASM keeps its scope until the browser runtime ends.
public sealed class UiSession : IDisposable
{
    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
    public bool IsDisposed { get; private set; }

    public void Dispose()
    {
        IsDisposed = true;
    }
}

public static class SharedServices
{
    public static IServiceCollection AddSampleUi(this IServiceCollection services)
    {
        services.AddScoped<NetWivCommands>();
        services.AddScoped<UiSession>();
        services.AddScoped<UiInteropCallbacks>();
        return services;
    }
}
