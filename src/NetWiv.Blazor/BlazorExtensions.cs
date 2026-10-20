using Microsoft.Extensions.DependencyInjection;
using NetWiv.Runtime;

namespace NetWiv.Blazor;

public static class BlazorExtensions
{
    /// <summary>Registers Microsoft's scoped Blazor WebView services in the application's root provider.</summary>
    public static IServiceCollection AddNetWivBlazor(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddBlazorWebView();
    }

    /// <summary>
    /// Hosts managed Razor components in one existing webview. Configure services with
    /// AddNetWivBlazor and supply a root provider through NetWiv.Hosting or UseServiceProvider.
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
