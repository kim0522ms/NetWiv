using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace NetWiv.BlazorHybrid.Aot.Hosting;

// Created once by each page scope. Bind before resolving application services so
// constructor-injected services resolve the same runtime and navigation manager.
internal sealed class AotPageServices
{
    private IJSRuntime? _runtime;
    private NavigationManager? _navigation;

    internal IJSRuntime Runtime => _runtime ?? throw new InvalidOperationException("The NativeAOT Blazor page is not attached.");
    internal NavigationManager Navigation => _navigation ?? throw new InvalidOperationException("The NativeAOT Blazor page is not attached.");

    internal void Configure(IJSRuntime runtime, NavigationManager navigation)
    {
        if (_runtime is not null) throw new InvalidOperationException("The NativeAOT Blazor page services are already configured.");
        _runtime = runtime;
        _navigation = navigation;
    }
}
