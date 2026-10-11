using NetWiv;

internal static class CommandThreadTests
{
    internal static async Task GuestDoesNotBlockUi()
    {
        foreach (var postMessage in new[] { false, true })
        {
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var returned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var runtime = new FakeRuntime();
            using var app = new Builder().UseRuntime(runtime).RegisterCommand("heavy", (_, _, _) =>
            {
                entered.TrySetResult(Environment.CurrentManagedThreadId);
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("UI callback blocked by command.");
                return ValueTask.FromResult(JsonValue.From("done"));
            }).Build();
            app.Run();
            Task<NetWiv.Runtime.AssetResponse>? response = null;
            var uiThread = new Thread(() =>
            {
                if (postMessage)
                {
                    const string marker = "__NETWIV_INVOKE_KEY__: '";
                    var script = runtime.Context!.InitializationScript;
                    var key = script.Substring(script.IndexOf(marker, StringComparison.Ordinal) + marker.Length, 64);
                    runtime.Context.OnMessage(new("main", new Uri("tauri://localhost"),
                        "{\"cmd\":\"heavy\",\"payload\":{},\"callback\":1,\"error\":2,\"__NETWIV_INVOKE_KEY__\":\"" + key + "\"}", "main"));
                }
                else response = GuestIpc.Invoke(runtime, "heavy", "{}");
                returned.SetResult();
            });
            uiThread.Start();
            try
            {
                var worker = await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
                await returned.Task.WaitAsync(TimeSpan.FromSeconds(1));
                Assert.True(worker != uiThread.ManagedThreadId, "Guest command ran on the native callback thread.");
            }
            finally { release.Set(); uiThread.Join(); }
            if (postMessage) Assert.Contains("done", await runtime.NextScript());
            else Assert.Equal("ok", (await response!).Headers!["Tauri-Response"]);
        }
    }
}
