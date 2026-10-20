using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using NetWiv.BlazorHybrid.Aot;
using NetWiv.BlazorHybrid.Aot.Hosting;
using NetWiv.Runtime;

namespace NetWiv.BlazorHybrid;

public static class BlazorExtensions
{
    /// <summary>Registers the default managed Hybrid host using Microsoft's scoped Blazor WebView services.</summary>
    public static IServiceCollection AddNetWivBlazorHybrid(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(service => service.ServiceType == typeof(BlazorAotOptions)))
            throw new InvalidOperationException("Choose either AddNetWivBlazorHybrid or AddNetWivBlazorHybridAot for one service provider.");
        return services.AddBlazorWebView();
    }

    /// <summary>
    /// Registers the experimental, statically configured Hybrid host for NativeAOT research.
    /// Application components, callback delegates and JSON metadata are explicit.
    /// </summary>
    /// <remarks>
    /// Prefer AddNetWivBlazorHybrid for ordinary managed applications. This opt-in host
    /// does not provide full Blazor API or component-library compatibility. NativeAOT package
    /// consumers must also set the NetWivBlazorExperimentalAot MSBuild property to true.
    /// </remarks>
    public static IServiceCollection AddNetWivBlazorHybridAot(this IServiceCollection services, Action<BlazorAotOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        if (services.Any(service => service.ServiceType == typeof(BlazorAotOptions) ||
            service.ServiceType == typeof(IJSRuntime) || service.ServiceType == typeof(NavigationManager)))
            throw new InvalidOperationException("Register one Blazor host before registering custom Blazor services.");
        var options = new BlazorAotOptions();
        configure(options);
        services.AddSingleton(options.Snapshot());
        services.AddLogging();
        services.AddScoped<AotPageServices>();
        services.AddScoped<IJSRuntime>(provider => provider.GetRequiredService<AotPageServices>().Runtime);
        services.AddScoped<NavigationManager>(provider => provider.GetRequiredService<AotPageServices>().Navigation);
        services.AddScoped<INavigationInterception, AotNavigationInterception>();
        services.AddScoped<IScrollToLocationHash, AotScrollToLocationHash>();
        return services;
    }

    /// <summary>
    /// Hosts Razor components in one existing webview. Configure services with
    /// AddNetWivBlazorHybrid or AddNetWivBlazorHybridAot and supply a root provider through
    /// NetWiv.Hosting or UseServiceProvider.
    /// </summary>
    public static Builder UseBlazor(this Builder builder, IDesktopRuntime runtime, BlazorWebViewOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        // Register the private scheme with Core as well as the runtime so normal NetWiv IPC
        // recognizes this bundled origin. The runtime decorator serves its actual assets.
        builder.RegisterUriSchemeProtocol("netwiv-blazor", static (_, _) =>
            new AssetResponse(404, "text/plain", "Not found"u8.ToArray()));
        return builder.UseRuntime(services => new BlazorDesktopRuntime(runtime, services, options));
    }
}
