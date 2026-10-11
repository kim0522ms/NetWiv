using NetWiv;

internal static class ResourceOwnershipTests
{
    internal static async Task CloseRequiresOwner()
    {
        var runtime = new FakeRuntime();
        var resource = new TrackedResource();
        using var app = new Builder().UseRuntime(runtime)
            .Window("main", "Main", "index.html").Window("secondary", "Secondary", "index.html")
            .RegisterCommand("owned", (context, _, _) => ValueTask.FromResult(JsonValue.From(context.AddResource(resource = new TrackedResource()))))
            .Capability(new("close", ["core:resources:default"], Windows: ["main", "secondary"])).Build();
        app.Run();
        var created = await GuestIpc.Invoke(runtime, "owned", "{}");
        Assert.Equal("ok", created.Headers!["Tauri-Response"]);
        var rid = JsonValue.Parse(GuestIpc.Body(created)).GetUInt32();
        var payload = "{\"rid\":" + rid + "}";
        var denied = await GuestIpc.Invoke(runtime, "plugin:resources|close", payload, webviewLabel: "secondary");
        Assert.Equal("error", denied.Headers!["Tauri-Response"]);
        Assert.True(!resource.Disposed && app.Handle.Resources.TryGet(rid) == resource, "Foreign close modified the owned resource.");
        var closed = await GuestIpc.Invoke(runtime, "plugin:resources|close", payload);
        Assert.Equal("ok", closed.Headers!["Tauri-Response"]);
        Assert.True(resource.Disposed, "Owner failed to close its resource.");

        // Backend-created resources remain explicitly shareable with guest code.
        var shared = new TrackedResource();
        var sharedId = app.Handle.Resources.Add(shared);
        var sharedClose = await GuestIpc.Invoke(runtime, "plugin:resources|close", "{\"rid\":" + sharedId + "}", webviewLabel: "secondary");
        Assert.Equal("ok", sharedClose.Headers!["Tauri-Response"]);
        Assert.True(shared.Disposed, "App-scoped resources stopped supporting guest close.");

        var backend = await GuestIpc.Invoke(runtime, "owned", "{}");
        var backendId = JsonValue.Parse(GuestIpc.Body(backend)).GetUInt32();
        await app.Handle.InvokeAsync("plugin:resources|close", JsonValue.Parse("{\"rid\":" + backendId + "}"));
        Assert.True(app.Handle.Resources.TryGet(backendId) is null, "Trusted backend could not close a document resource.");
        Assert.True(resource.Disposed, "Trusted close did not dispose the owned resource.");
    }

    private sealed class TrackedResource : IDisposable
    {
        internal bool Disposed;
        public void Dispose() => Disposed = true;
    }
}
