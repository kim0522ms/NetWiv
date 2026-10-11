using System.Net;
using NetWiv;
using NetWiv.Plugin.Localhost;

internal static class LocalhostPluginTests
{
    internal static async Task ServesAssets()
    {
        var plugin = new LocalhostPlugin(0, onRequest: (request, response) =>
        {
            if (request.Url == "/") response.AddHeader("X-NetWiv-Test", "asset");
        });
        using var app = new Builder().UseRuntime(new FakeRuntime())
            .UseEmbeddedAssets(typeof(LocalhostPluginTests).Assembly).Plugin(plugin).Build();
        app.Run();
        using var http = new HttpClient();
        var url = "http://127.0.0.1:" + plugin.BoundPort + "/";
        using var response = await http.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("window.embedded=true", await response.Content.ReadAsStringAsync());
        Assert.Equal("asset", response.Headers.GetValues("X-NetWiv-Test").Single());
        using var post = await http.PostAsync(url, null);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, post.StatusCode);
        using var head = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        app.Dispose();
        var recycled = new System.Net.Sockets.TcpListener(IPAddress.Loopback, plugin.BoundPort)
        { ExclusiveAddressUse = true };
        recycled.Start();
        recycled.Stop();
    }
}
