using NetWiv;
using NetWiv.BlazorHybrid.Contracts;
using NetWiv.Hosting;
using NetWiv.Platform.Desktop;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
#if NETWIV_BLAZOR_HYBRID
using NetWiv.BlazorHybrid;
using NetWiv.BlazorHybrid.Shared;
#endif

[assembly: CommandJsonContext(typeof(SampleJsonContext))]
[assembly: GenerateFrontendBindings]

namespace NetWiv.BlazorHybrid.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var builder = NetWivApplication.CreateBuilder(args);
        builder.Logging.ClearProviders().AddConsole();
        builder.Services.AddSingleton<GreetingSequence>();
        builder.Services.AddScoped<GreetingService>();
        builder.Services.AddScoped<GreetingCommands>();
        builder.Desktop
            .Metadata(new("NetWiv shared Blazor sample", "0.1.0", "dev.netwiv.blazorsample"))
            .InvokeHandler("greet")
            .Capability(new("main-close", ["core:window:allow-close"], Webviews: ["main"]));

#if NETWIV_BLAZOR_HYBRID
#if NETWIV_BLAZOR_EXPERIMENTAL_AOT
        builder.Services.AddNetWivBlazorHybridAot(HybridUiRegistration.Configure);
        builder.Services.AddScoped<IUiInteropReferenceFactory, HybridUiInteropReferenceFactory>();
#else
        builder.Services.AddNetWivBlazorHybrid();
        builder.Services.AddScoped<IUiInteropReferenceFactory, BrowserUiInteropReferenceFactory>();
#endif
        builder.Services.AddSampleUi();
        var options = new BlazorWebViewOptions
        {
            HostPage = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html"),
            WebviewLabel = "main",
        };
        options.RootComponents.Add<Shared.App>("#app");
        builder.Desktop
            .UseBlazor(new DesktopRuntime(), options)
            .Window("main", "NetWiv Blazor Hybrid", options.AppBaseUri.AbsoluteUri, 880, 780);
#else
        var root = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        builder.Desktop
            .UseRuntime(new DesktopRuntime())
            .ResolveAssets(request => SampleAssets.Resolve(root, request))
            .Window("main", "NetWiv Blazor WebAssembly", "tauri://localhost/index.html", 880, 780);
#endif

        using var app = builder.Build();
        app.Run();
    }
}
