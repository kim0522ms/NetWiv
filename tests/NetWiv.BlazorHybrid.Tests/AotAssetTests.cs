using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using NetWiv.BlazorHybrid;
using NetWiv.BlazorHybrid.Aot;
using NetWiv.Runtime;

internal static class AotAssetTests
{
    internal static Task Assets()
    {
        using var files = new BorrowedFiles(new PhysicalFileProvider(Path.Combine(AppContext.BaseDirectory, "fixtures")));
        var options = new BlazorWebViewOptions { FileProvider = files, HostPage = "index.html" };
        options.RootComponents.Add<EmptyRoot>("#app");
        using (var assets = new AotStaticFiles(options))
        {
            var host = assets.TryGetResponse(Request("index.html"))!;
            Check.Equal(200, host.StatusCode);
            Check.Contains("hybrid-test-root", Encoding.UTF8.GetString(host.Body.Span));
            var route = assets.TryGetResponse(Request("nested/route"))!;
            Check.Equal(200, route.StatusCode);
            Check.Equal(Encoding.UTF8.GetString(host.Body.Span), Encoding.UTF8.GetString(route.Body.Span));
            var css = assets.TryGetResponse(Request("app.css"))!;
            Check.Contains("text/css", css.ContentType);
            Check.Contains("color: green", Encoding.UTF8.GetString(css.Body.Span));
            Check.Equal(404, assets.TryGetResponse(Request("missing.js"))!.StatusCode);
            Check.Equal(405, assets.TryGetResponse(Request("index.html") with { Method = "POST" })!.StatusCode);
            Check.Equal(400, assets.TryGetResponse(Request("folder%5c..%5cindex.html"))!.StatusCode);
            Check.True(assets.TryGetResponse(Request("index.html", "https://foreign.invalid/")) is null,
                "Foreign origins must not receive Hybrid assets.");
            Check.True(assets.TryGetResponse(Request("index.html", "http://netwiv-blazor.localhost:8443/")) is null,
                "Different ports must not receive Hybrid assets.");
            var framework = assets.TryGetResponse(Request("_framework/blazor.webview.js"))!;
            Check.Equal(200, framework.StatusCode);
            Check.Contains("javascript", framework.ContentType);
            Check.True(framework.Body.Length > 1000, "The pinned embedded transport was not served.");
            Check.Equal(404, assets.TryGetResponse(Request("_framework/missing"))!.StatusCode);
        }
        Check.True(!files.Disposed, "The host disposed an application-owned file provider.");
        return Task.CompletedTask;
    }

    internal static Task Registration()
    {
        var options = new BlazorAotOptions();
        options.Components.Register<EmptyRoot>();
        var snapshot = options.Snapshot();
        Check.Throws<InvalidOperationException>(() => snapshot.Components.Register<EmptyRoot>());
        Check.Throws<ArgumentException>(() => options.Components.Register<EmptyRoot>());
        var managed = new ServiceCollection().AddNetWivBlazorHybrid();
        Check.Throws<InvalidOperationException>(() => managed.AddNetWivBlazorHybridAot(_ => { }));
        var aot = new ServiceCollection().AddNetWivBlazorHybridAot(_ => { });
        Check.Throws<InvalidOperationException>(() => aot.AddNetWivBlazorHybrid());
        Check.Throws<InvalidOperationException>(() => aot.AddNetWivBlazorHybridAot(_ => { }));
        return Task.CompletedTask;
    }

    private static AssetRequest Request(string path, string? origin = null) =>
        new(new Uri(origin is null ? new BlazorWebViewOptions().AppBaseUri : new Uri(origin), path), "GET", new Dictionary<string, string>());

    private sealed class BorrowedFiles(IFileProvider provider) : IFileProvider, IDisposable
    {
        internal bool Disposed { get; private set; }
        public IFileInfo GetFileInfo(string subpath) => provider.GetFileInfo(subpath);
        public IDirectoryContents GetDirectoryContents(string subpath) => provider.GetDirectoryContents(subpath);
        public IChangeToken Watch(string filter) => provider.Watch(filter);
        public void Dispose()
        {
            Disposed = true;
            (provider as IDisposable)?.Dispose();
        }
    }
}
