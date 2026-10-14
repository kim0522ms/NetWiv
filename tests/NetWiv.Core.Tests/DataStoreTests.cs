using NetWiv;

internal static class DataStoreTests
{
    internal static async Task ErrorBoundaries()
    {
        var uuid = "{\"uuid\":[0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15]}";
        foreach (var error in new[] { "data store is currently opened", "WKWebsiteDataStore identifier management requires macOS 14 or later.", "other deletion failure" })
        {
            var runtime = new FakeRuntime { InvokeHandler = (_, _) => throw new InvokeException(error) };
            using var app = new Builder().UseRuntime(runtime)
                .Capability(new("data-store", ["core:app:allow-remove-data-store"], Windows: ["main"])).Build();
            app.Run();
            var response = await GuestIpc.Invoke(runtime, "plugin:app|remove_data_store", uuid);
            Assert.Equal("error", response.Headers!["Tauri-Response"]);
            Assert.Equal(JsonValue.From(error == "data store is currently opened" ? "runtime error: failed to remove data store" : error).GetRawText(), GuestIpc.Body(response));
            try
            {
                await AppApi.RemoveDataStoreAsync(app.Handle, new DataStoreIdentifier(Enumerable.Range(0, 16).Select(value => (byte)value).ToArray()));
                throw new Exception("Backend deletion unexpectedly succeeded.");
            }
            catch (InvokeException actual) { Assert.Equal(error, actual.Message); }
        }
        foreach (var error in new Exception[] { new OperationCanceledException("cancelled"), new InvokeException(JsonValue.Parse("{\"message\":\"data store is currently opened\"}")) })
        {
            var runtime = new FakeRuntime { InvokeHandler = (_, _) => throw error };
            using var app = new Builder().UseRuntime(runtime)
                .Capability(new("data-store", ["core:app:allow-remove-data-store"], Windows: ["main"])).Build();
            app.Run();
            var response = await GuestIpc.Invoke(runtime, "plugin:app|remove_data_store", uuid);
            Assert.Equal("error", response.Headers!["Tauri-Response"]);
            Assert.DoesNotContain("runtime error: failed to remove data store", GuestIpc.Body(response));
            if (error is InvokeException structured) Assert.Equal(structured.ErrorPayload.GetRawText(), GuestIpc.Body(response));
            else Assert.Contains("cancelled", GuestIpc.Body(response));
        }
        var calls = 0;
        var invalidRuntime = new FakeRuntime { InvokeHandler = (_, _) => { calls++; return JsonValue.Null; } };
        using var invalidApp = new Builder().UseRuntime(invalidRuntime)
            .Capability(new("data-store", ["core:app:allow-remove-data-store"], Windows: ["main"])).Build();
        invalidApp.Run();
        foreach (var payload in new[] { "{\"uuid\":[]}", "{\"uuid\":[256,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15]}" })
        {
            var response = await GuestIpc.Invoke(invalidRuntime, "plugin:app|remove_data_store", payload);
            Assert.Equal("error", response.Headers!["Tauri-Response"]);
            Assert.DoesNotContain("runtime error: failed to remove data store", GuestIpc.Body(response));
        }
        Assert.Equal(0, calls);
    }
}
