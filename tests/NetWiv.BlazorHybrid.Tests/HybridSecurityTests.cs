using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using NetWiv.BlazorHybrid;
using NetWiv.Runtime;

internal static class HybridSecurityTests
{
    internal static async Task AssetsAndIsolation()
    {
        await using var services = new ServiceCollection().AddNetWivBlazorHybrid().BuildServiceProvider();
        using var files = new PhysicalFileProvider(Path.Combine(AppContext.BaseDirectory, "fixtures"));
        var options = new BlazorWebViewOptions { FileProvider = files, HostPage = "index.html" };
        options.RootComponents.Add<SecurityRoot>("#app");
        var native = new FakeRuntime();
        var runtime = new BlazorDesktopRuntime(native, services, options);
        var policyCalls = 0;
        var context = new RuntimeContext([new("main", "Main", "index.html", 100, 100, default)],
            "", _ => { }, _ => { }, _ => null)
        {
            ApplyAssetPolicy = (request, response) =>
            {
                policyCalls++;
                Check.Equal("netwiv-blazor", request.Uri.Scheme);
                var headers = response.Headers is null ? new Dictionary<string, string>() : new Dictionary<string, string>(response.Headers);
                headers["Cross-Origin-Opener-Policy"] = "same-origin";
                if (response.ContentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
                {
                    headers["Content-Security-Policy"] = "default-src 'none'";
                    var body = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(response.Body.Span) + "<!-- policy-applied -->");
                    headers["Content-Length"] = body.Length.ToString();
                    return response with { Body = body, Headers = headers };
                }
                return response with { Headers = headers };
            }
        };
        var run = Task.Run(() => runtime.Run(context));
        try
        {
            var wrapped = await native.Ready.Task.WaitAsync(TimeSpan.FromSeconds(3));
            wrapped.OnDocumentChanged!(new("main", 1, false));
            foreach (var path in new[] { "index.html", "app.css", "_framework/blazor.webview.js" })
            {
                var response = await wrapped.ResolveUriScheme!(new(new Uri("netwiv-blazor://localhost/" + path), "GET",
                    new Dictionary<string, string>(), WebviewLabel: "main", DocumentId: 1));
                Check.Equal(200, response!.StatusCode);
                Check.Equal("same-origin", response.Headers!["Cross-Origin-Opener-Policy"]);
                if (path == "index.html")
                {
                    Check.Equal("default-src 'none'", response.Headers["Content-Security-Policy"]);
                    Check.Contains("hybrid-test-root", Encoding.UTF8.GetString(response.Body.Span));
                    Check.Contains("policy-applied", Encoding.UTF8.GetString(response.Body.Span));
                }
            }
            var head = await wrapped.ResolveUriScheme!(new(new Uri("netwiv-blazor://localhost/index.html"), "HEAD",
                new Dictionary<string, string>(), WebviewLabel: "main", DocumentId: 1));
            Check.Equal(0, head!.Body.Length);
            Check.Equal("default-src 'none'", head.Headers!["Content-Security-Policy"]);
            Check.True(int.Parse(head.Headers["Content-Length"]) > 0, "HEAD lost the policy-transformed representation length.");
            Check.Equal(4, policyCalls);
        }
        finally
        {
            native.Return.Set();
            await run;
            native.Return.Dispose();
        }

        var isolatedNative = new FakeRuntime();
        var isolated = new BlazorDesktopRuntime(isolatedNative, services, options);
        Check.Throws<NotSupportedException>(() => isolated.Run(context with { IsolationProtocol = "isolation-test" }));
        Check.True(isolatedNative.Context is null, "Hybrid started the native runtime despite unsupported isolation.");
        isolatedNative.Return.Dispose();
    }

    private sealed class SecurityRoot : IComponent
    {
        public void Attach(RenderHandle renderHandle) { }
        public Task SetParametersAsync(ParameterView parameters) => Task.CompletedTask;
    }
}
