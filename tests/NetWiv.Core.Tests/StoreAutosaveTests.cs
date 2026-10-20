using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NetWiv;
using NetWiv.Plugin.Store;

internal static class StoreAutosaveTests
{
    internal static async Task Options()
    {
        using var fixture = new Fixture();
        foreach (var option in new[] { "{}", "{\"autoSave\":true}", "{\"autoSave\":null}" })
        {
            var name = Guid.NewGuid().ToString("N") + ".json";
            var rid = await fixture.Load(name, option);
            var started = Stopwatch.StartNew();
            await fixture.Set(rid, "answer", 42);
            await Until(() => File.Exists(fixture.PathFor(name)));
            Assert.True(started.ElapsedMilliseconds >= 90, "Default autosave did not debounce for 100ms.");
        }

        var immediate = await fixture.Load("immediate.json", "{\"autoSave\":0}");
        await fixture.Set(immediate, "answer", 42);
        Assert.Equal(42, Read(fixture.PathFor("immediate.json"), "answer"));

        foreach (var option in new[] { "-1", "1.5", "\"100\"", "{}", "18446744073709551616" })
        {
            try
            {
                await fixture.Load(Guid.NewGuid().ToString("N"), "{\"autoSave\":" + option + "}");
                throw new Exception("Invalid autoSave accepted: " + option);
            }
            catch (InvokeException) { }
        }
        var maximum = await fixture.Load("maximum.json", "{\"autoSave\":18446744073709551615}");
        await fixture.Set(maximum, "answer", 42);
        Assert.True(!File.Exists(fixture.PathFor("maximum.json")), "Large debounce saved immediately.");

        var failed = await fixture.Load("immediate-failure.json", "{\"autoSave\":0}");
        Directory.CreateDirectory(fixture.PathFor("immediate-failure.json"));
        try { await fixture.Set(failed, "answer", 42); throw new Exception("An immediate failed save succeeded."); }
        catch (IOException) { }
        Assert.Equal(42, (await fixture.Command("get", failed, ",\"key\":\"answer\""))[0].GetInt32());
        Directory.Delete(fixture.PathFor("immediate-failure.json"));
        await fixture.Command("save", failed);
        Assert.Equal(42, Read(fixture.PathFor("immediate-failure.json"), "answer"));
    }

    internal static async Task DebounceAndMutations()
    {
        using var fixture = new Fixture();
        var rid = await fixture.Load("debounce.json", "{\"autoSave\":400,\"defaults\":{\"answer\":1}}");
        await fixture.Set(rid, "answer", 2);
        await Task.Delay(120);
        var started = Stopwatch.StartNew();
        await fixture.Set(rid, "answer", 3);
        var path = fixture.PathFor("debounce.json");
        await Until(() => File.Exists(path));
        Assert.True(started.ElapsedMilliseconds >= 350, "A previous timer saved before the latest mutation's debounce expired.");
        Assert.Equal(3, Read(path, "answer"));

        await fixture.Command("delete", rid, ",\"key\":\"answer\"");
        await Until(() => ReadText(path) == "{}");
        await fixture.Command("reset", rid);
        await Until(() => Read(path, "answer") == 1);
        await fixture.Command("clear", rid);
        await Until(() => ReadText(path) == "{}");
    }

    internal static async Task PollingReadRetriesMissingFile()
    {
        using var fixture = new Fixture();
        var path = fixture.PathFor("poll.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var attempts = 0;
        await Until(() =>
        {
            if (++attempts == 2)
            {
                File.WriteAllText(path, "{\"answer\":42}");
            }
            return Read(path, "answer") == 42;
        });
        Assert.Equal(2, attempts);

        // Only transient file availability errors are retried, not corrupt data
        // or unrelated I/O failures.
        File.WriteAllText(path, "invalid JSON");
        try { await Until(() => Read(path, "answer") == 42); throw new Exception("Invalid JSON was accepted."); }
        catch (JsonException) { }
        var failure = new IOException("Non-transient test failure.");
        try { await Until(() => throw failure); throw new Exception("An I/O failure was accepted."); }
        catch (IOException error) when (ReferenceEquals(error, failure)) { }
    }

    internal static async Task ExplicitSaveCancelsTimer()
    {
        using var fixture = new Fixture();
        var rid = await fixture.Load("explicit.json", "{\"autoSave\":150}");
        await fixture.Set(rid, "answer", 42);
        await fixture.Command("save", rid);
        var path = fixture.PathFor("explicit.json");
        Assert.Equal(42, Read(path, "answer"));
        File.WriteAllText(path, "{\"answer\":7}");
        await Task.Delay(300);
        Assert.Equal(7, Read(path, "answer"));
        fixture.App.Dispose();
        Assert.Equal(7, Read(path, "answer"));
    }

    internal static async Task ReloadCancelsPendingWrites()
    {
        foreach (var existing in new[] { false, true })
        foreach (var shutdown in new[] { false, true })
        {
            using var fixture = new Fixture();
            var path = fixture.PathFor("reload.json");
            if (existing)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "{\"answer\":7}");
            }
            var rid = await fixture.Load("reload.json",
                "{\"autoSave\":" + (shutdown ? 60000 : 500) + ",\"defaults\":{\"answer\":1}}");
            await fixture.Set(rid, "answer", 42);
            await fixture.Command("reload", rid);
            Assert.Equal(existing ? 7 : 1, (await fixture.Command("get", rid, ",\"key\":\"answer\""))[0].GetInt32());
            // An external edit after reload must survive both a stale timer and shutdown.
            if (existing) File.WriteAllText(path, "{\"answer\":9}");
            if (shutdown) fixture.App.Dispose();
            else await Task.Delay(800);
            if (existing) Assert.Equal(9, Read(path, "answer"));
            else Assert.True(!File.Exists(path), "Reloaded changes caused an unwanted store write.");

            if (!shutdown)
            {
                await fixture.Set(rid, "answer", 10);
                await Until(() => File.Exists(path) && Read(path, "answer") == 10);
            }
        }
    }

    internal static async Task FailedReloadKeepsPendingWrites()
    {
        foreach (var shutdown in new[] { false, true })
        {
            using var fixture = new Fixture();
            var rid = await fixture.Load("reload-failure.json", "{\"autoSave\":" + (shutdown ? 60000 : 500) + "}");
            var path = fixture.PathFor("reload-failure.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "invalid JSON");
            await fixture.Set(rid, "answer", 42);
            try { await fixture.Command("reload", rid); throw new Exception("Invalid store reload succeeded."); }
            catch (InvokeException) { }
            Assert.Equal(42, (await fixture.Command("get", rid, ",\"key\":\"answer\""))[0].GetInt32());
            if (shutdown) fixture.App.Dispose();
            else await Until(() => ReadText(path).StartsWith('{'));
            Assert.Equal(42, Read(path, "answer"));
        }
    }

    internal static async Task SharedHandlesAndShutdown()
    {
        using var fixture = new Fixture();
        var rid = await fixture.Load("shared.json", "{\"autoSave\":60000}");
        var second = await fixture.Load("shared.json", "{\"autoSave\":0}");
        var third = (await fixture.App.Handle.InvokeAsync("plugin:store|get_store", JsonValue.Parse(
            "{\"path\":" + JsonValue.From(fixture.Name + "/shared.json").GetRawText() + "}"))).GetUInt32();
        await Task.WhenAll(Enumerable.Range(0, 48).Select(index => Task.Run(async () =>
            await fixture.Set(index % 2 == 0 ? rid : second, "key" + index, index))));
        Assert.True(!File.Exists(fixture.PathFor("shared.json")), "Later load options replaced the first store's debounce.");
        fixture.App.Handle.Resources.Close(rid);
        await fixture.Set(third, "answer", 42);
        var manual = await fixture.Load("manual.json", "{\"autoSave\":false}");
        await fixture.Set(manual, "answer", 7);
        await fixture.Command("save", manual);
        await fixture.Set(manual, "answer", 8);
        fixture.App.Dispose();
        fixture.Plugin.Dispose();
        using var saved = JsonDocument.Parse(File.ReadAllText(fixture.PathFor("shared.json")));
        Assert.Equal(49, saved.RootElement.EnumerateObject().Count());
        Assert.Equal(42, saved.RootElement.GetProperty("answer").GetInt32());
        Assert.Equal(7, Read(fixture.PathFor("manual.json"), "answer"));
    }

    internal static async Task FailedSaveRetries()
    {
        var records = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(logging => logging.AddProvider(records));
        using var fixture = new Fixture(factory);
        foreach (var shutdown in new[] { false, true })
        {
            var name = shutdown ? "shutdown-retry.json" : "explicit-retry.json";
            var rid = await fixture.Load(name, "{\"autoSave\":20}");
            var path = fixture.PathFor(name);
            Directory.CreateDirectory(path);
            var count = records.Records.Count;
            await fixture.Set(rid, "answer", 42);
            await Until(() => records.Records.Count > count);
            var warning = records.Records.Last();
            Assert.Equal("NetWiv.Store", warning.Category);
            Assert.Equal(LogLevel.Warning, warning.Level);
            Assert.True(warning.Exception is null && !warning.Message.Contains(path, StringComparison.Ordinal), "Background warning exposed a store path.");
            Assert.Equal(0, Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp").Length);
            try { await fixture.Command("save", rid); throw new Exception("An explicit failed save succeeded."); }
            catch (IOException) { }
            Directory.Delete(path);
            if (shutdown) fixture.App.Dispose();
            else await fixture.Command("save", rid);
            Assert.Equal(42, Read(path, "answer"));
        }
    }

    internal static async Task DisposalRacesAndFailure()
    {
        using (var fixture = new Fixture())
        {
            var bad = await fixture.Load("bad.json", "{\"autoSave\":60000}");
            var good = await fixture.Load("good.json", "{\"autoSave\":60000}");
            await fixture.Set(bad, "answer", 1);
            await fixture.Set(good, "answer", 2);
            Directory.CreateDirectory(fixture.PathFor("bad.json"));
            Assert.Throws<AggregateException>(fixture.App.Dispose);
            Assert.Equal(2, Read(fixture.PathFor("good.json"), "answer"));
            fixture.Plugin.Dispose();
        }

        for (var attempt = 0; attempt < 8; attempt++)
        {
            using var fixture = new Fixture();
            var rid = await fixture.Load("race.json", "{\"autoSave\":1}");
            await fixture.Set(rid, "seed", 1);
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var completed = 0;
            var mutations = Enumerable.Range(0, 32).Select(index => Task.Run(async () =>
            {
                await start.Task;
                try
                {
                    await fixture.Set(rid, "key" + index, index);
                    Interlocked.Increment(ref completed);
                }
                catch (ObjectDisposedException) { }
            })).ToArray();
            var disposal = Task.Run(async () => { await start.Task; fixture.Plugin.Dispose(); });
            start.SetResult();
            await Task.WhenAll(mutations.Append(disposal)).WaitAsync(TimeSpan.FromSeconds(5));
            var path = fixture.PathFor("race.json");
            using var saved = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal(completed + 1, saved.RootElement.EnumerateObject().Count());
            File.WriteAllText(path, "{}");
            await Task.Delay(20);
            Assert.Equal("{}", File.ReadAllText(path));
        }
    }

    private static int Read(string path, string key)
    {
        using var document = JsonDocument.Parse(ReadText(path));
        return document.RootElement.TryGetProperty(key, out var value) ? value.GetInt32() : int.MinValue;
    }

    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            try { if (condition()) return; }
            // An asynchronous publication/replacement may make the path unavailable
            // during a poll. Persistent absence still fails at the original deadline.
            catch (FileNotFoundException) { }
            // ReplaceFile can briefly hold the destination while the poll opens it.
            catch (IOException error) when (OperatingSystem.IsWindows() && (error.HResult & 0xffff) is 32 or 33) { }
            await Task.Delay(5, timeout.Token);
        }
    }

    private static string ReadText(string path)
    {
        // Polling must allow the autosave writer to atomically replace the file.
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(file);
        return reader.ReadToEnd();
    }

    private sealed class Fixture : IDisposable
    {
        public readonly string Name = "netwiv-store-" + Guid.NewGuid().ToString("N");
        public readonly StorePlugin Plugin = new();
        public readonly App App;
        private readonly string _root;

        public Fixture(ILoggerFactory? loggerFactory = null)
        {
            var builder = new Builder().UseRuntime(new FakeRuntime()).Plugin(Plugin);
            if (loggerFactory is not null) builder.UseLoggerFactory(loggerFactory);
            App = builder.Build();
            _root = App.Handle.ResolveDirectoryAsync((int)BaseDirectory.AppData, Name).GetAwaiter().GetResult();
        }

        public string PathFor(string name) => Path.Combine(_root, name);
        public async Task<uint> Load(string name, string options) => (await App.Handle.InvokeAsync("plugin:store|load", JsonValue.Parse(
            "{\"path\":" + JsonValue.From(Name + "/" + name).GetRawText() + ",\"options\":" + options + "}"))).GetUInt32();
        public Task<JsonElement> Command(string name, uint rid, string tail = "") =>
            App.Handle.InvokeAsync("plugin:store|" + name, JsonValue.Parse("{\"rid\":" + rid + tail + "}")).AsTask();
        public Task<JsonElement> Set(uint rid, string key, int value) => Command("set", rid, ",\"key\":" + JsonValue.From(key).GetRawText() + ",\"value\":" + value);
        public void Dispose()
        {
            try { App.Dispose(); }
            finally { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
        }
    }
}
