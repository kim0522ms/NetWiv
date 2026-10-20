using Microsoft.AspNetCore.Components.WebView;
using Microsoft.Extensions.FileProviders;

namespace NetWiv.BlazorHybrid;

internal static class BlazorFrameworkFiles
{
    internal static IFileProvider Create() =>
#if NET10_0_OR_GREATER
        new EmbeddedFileProvider(typeof(BlazorFrameworkFiles).Assembly, "NetWiv.BlazorHybrid");
#else
        new ManifestEmbeddedFileProvider(typeof(WebViewManager).Assembly);
#endif
}
