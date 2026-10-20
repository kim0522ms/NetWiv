using System.Diagnostics;
using System.Text.Json;
using NetWiv;
using NetWiv.Plugin.Fs;

internal static class FsWatchTests
{
    internal static async Task Events()
    {
        var root = Directory.CreateTempSubdirectory("netwiv-watch-").FullName;
        try
        {
            var runtime = new FakeRuntime();
            using var app = new Builder().UseRuntime(runtime).Plugin(new FsPlugin()).Build();
            app.Run();
            var rid = (await app.Handle.InvokeAsync("plugin:fs|watch", Args([Path.GetRelativePath(Path.GetTempPath(), root)], "{\"baseDir\":12}"))).GetUInt32();
            var file = Path.Combine(root, "created.txt");
            File.WriteAllText(file, "one");
            await Until(runtime, e => Kind(e, "create") && Paths(e).Contains(file));
            File.AppendAllText(file, "two");
            await Until(runtime, e => Kind(e, "modify") && Paths(e).Contains(file));
            var renamed = Path.Combine(root, "renamed.txt");
            File.Move(file, renamed);
            var change = await Until(runtime, e => Kind(e, "modify") && e.GetProperty("type").GetProperty("modify").GetProperty("kind").GetString() == "rename");
            Assert.True(Paths(change).SequenceEqual(new[] { file, renamed }), "Rename omitted its old or new path.");
            File.Delete(renamed);
            await Until(runtime, e => Kind(e, "remove") && Paths(e).Contains(renamed));
            await app.Handle.InvokeAsync("plugin:resources|close", JsonValue.Parse($"{{\"rid\":{rid}}}"));
            await Drain(runtime);
            File.WriteAllText(file, "after close");
            await Silent(runtime);
        }
        finally { Directory.Delete(root, true); }
    }

    internal static async Task RecursionAndMultipleFiles()
    {
        var root = Directory.CreateTempSubdirectory("netwiv-watch-").FullName;
        try
        {
            var nested = Directory.CreateDirectory(Path.Combine(root, "nested")).FullName;
            var child = Path.Combine(nested, "child.txt");
            File.WriteAllText(child, "initial");
            var other = Path.Combine(root, "other.txt"); File.WriteAllText(other, "initial");
            var runtime = new FakeRuntime();
            using var app = new Builder().UseRuntime(runtime).Plugin(new FsPlugin()).Build(); app.Run();
            var rid = (await app.Handle.InvokeAsync("plugin:fs|watch", Args([root]))).GetUInt32();
            File.AppendAllText(child, "not recursive");
            await Task.Delay(250);
            while (runtime.PendingScripts > 0)
            {
                var value = Parse(await runtime.NextScript());
                Assert.True(!Paths(value.GetProperty("message")).Contains(child), "Nonrecursive watch reported a nested file.");
            }
            app.Handle.Resources.Close(rid); await Drain(runtime);
            rid = (await app.Handle.InvokeAsync("plugin:fs|watch", Args([root], "{\"recursive\":true}"))).GetUInt32();
            File.AppendAllText(child, "recursive"); await Until(runtime, e => Paths(e).Contains(child));
            var added = Directory.CreateDirectory(Path.Combine(root, "added", "deep")).FullName;
            await Until(runtime, e => e.GetProperty("attrs").TryGetProperty("rescan", out var rescan) && rescan.GetBoolean());
            await Drain(runtime);
            var addedFile = Path.Combine(added, "new.txt"); File.WriteAllText(addedFile, "new descendant");
            await Until(runtime, e => Paths(e).Contains(addedFile));
            app.Handle.Resources.Close(rid); await Drain(runtime);
            rid = (await app.Handle.InvokeAsync("plugin:fs|watch", Args([new Uri(child).AbsoluteUri, other]))).GetUInt32();
            File.WriteAllText(Path.Combine(root, "unrelated.txt"), "unrelated"); await Silent(runtime);
            File.AppendAllText(child, "first"); await Until(runtime, e => Paths(e).Contains(child));
            File.AppendAllText(other, "second"); await Until(runtime, e => Paths(e).Contains(other));
            app.Handle.Resources.Close(rid);
        }
        finally { Directory.Delete(root, true); }
    }

    internal static async Task Debounce()
    {
        var root = Directory.CreateTempSubdirectory("netwiv-watch-").FullName;
        try
        {
            var file = Path.Combine(root, "existing.txt"); File.WriteAllText(file, "initial");
            var runtime = new FakeRuntime();
            using var app = new Builder().UseRuntime(runtime).Plugin(new FsPlugin()).Build(); app.Run();
            var rid = (await app.Handle.InvokeAsync("plugin:fs|watch", Args([file], "{\"delayMs\":300}"))).GetUInt32();
            var elapsed = Stopwatch.StartNew();
            for (var i = 0; i < 10; i++) File.AppendAllText(file, "update");
            await Task.Delay(100); Assert.Equal(0, runtime.PendingScripts);
            var first = await Until(runtime, e => Paths(e).Contains(file));
            Assert.True(elapsed.ElapsedMilliseconds >= 250, "Debounced event arrived before the batch delay.");
            string Tuple(JsonElement value) => value.GetProperty("type").GetRawText() + "\0" + string.Join('\0', Paths(value));
            var seen = new HashSet<string>(StringComparer.Ordinal) { Tuple(first) };
            // A native backend can report distinct create/modify kinds for the
            // same path. Those are separate tuples in the first batch.
            await Task.Delay(100);
            while (runtime.PendingScripts > 0)
            {
                var value = Parse(await runtime.NextScript()).GetProperty("message");
                Assert.True(seen.Add(Tuple(value)), "A debounce batch repeated an identical kind/path tuple.");
            }
            File.AppendAllText(file, "cancel pending");
            app.Handle.Resources.Close(rid); await Drain(runtime); await Silent(runtime, 400);
        }
        finally { Directory.Delete(root, true); }
    }

    internal static async Task ScopeAndOwnership()
    {
        var root = Directory.CreateTempSubdirectory("netwiv-watch-").FullName;
        try
        {
            var denied = Path.Combine(root, "private.txt");
            var deniedDirectory = Directory.CreateDirectory(Path.Combine(root, "private")).FullName;
            var runtime = new FakeRuntime();
            using var app = Scoped(runtime, new FsPlugin(), root,
                [JsonValue.From(denied), JsonValue.From(deniedDirectory), JsonValue.From(deniedDirectory + "/**")]); app.Run();
            runtime.Context!.OnDocumentChanged!(new("main", 1, false));
            var rid = await GuestWatch(runtime, Args([root], "{\"recursive\":true}"), 1);
            File.WriteAllText(denied, "hidden"); await Silent(runtime);
            var deniedChild = Path.Combine(deniedDirectory, "child.txt");
            File.WriteAllText(deniedChild, "hidden descendant");
            File.Delete(deniedChild);
            await Task.Delay(250);
            while (runtime.PendingScripts > 0)
            {
                var hint = Parse(await runtime.NextScript()).GetProperty("message");
                Assert.Equal(0, Paths(hint).Length);
                Assert.True(hint.GetProperty("attrs").GetProperty("rescan").GetBoolean(), "Private topology disclosed a detailed event.");
            }
            var visible = Path.Combine(root, "visible.txt"); File.WriteAllText(visible, "visible");
            await Until(runtime, e => Paths(e).Contains(visible));
            File.Move(visible, denied, true);
            var renamed = await Until(runtime, e => Paths(e).Contains(visible) ||
                e.GetProperty("attrs").TryGetProperty("rescan", out var rescan) && rescan.GetBoolean());
            Assert.True(!Paths(renamed).Contains(denied), "Rename disclosed its denied endpoint.");
            await Task.Delay(100);
            while (runtime.PendingScripts > 0)
            {
                var change = Parse(await runtime.NextScript()).GetProperty("message");
                Assert.True(!Paths(change).Contains(denied), "Overwrite disclosed its denied endpoint.");
            }
            var deniedRegistration = await GuestIpc.Invoke(runtime, "plugin:fs|watch", Args([denied]).GetRawText(), 1);
            Assert.Equal("error", deniedRegistration.Headers!["Tauri-Response"]);
            var wrongOwner = await GuestIpc.Invoke(runtime, "plugin:resources|close", $"{{\"rid\":{rid}}}", 1, "other");
            Assert.Equal("error", wrongOwner.Headers!["Tauri-Response"]);
            Assert.True(app.Handle.Resources.TryGet(rid) is not null, "Another document closed the watch.");
            runtime.Context.OnDocumentChanged(new("main", 2, false));
            Assert.True(app.Handle.Resources.TryGet(rid) is null, "Reload retained a watch resource.");
            File.WriteAllText(visible, "after reload"); await Silent(runtime);
            rid = await GuestWatch(runtime, Args([root]), 2);
            runtime.Context.OnDocumentChanged(new("main", 2, true));
            Assert.True(app.Handle.Resources.TryGet(rid) is null, "Webview close retained a watch resource.");
            File.AppendAllText(visible, "after close"); await Silent(runtime);
            var nested = Directory.CreateDirectory(Path.Combine(root, "patterns")).FullName;
            var patternRuntime = new FakeRuntime();
            using var patternApp = new Builder().UseRuntime(patternRuntime).Plugin(new FsPlugin()).Window("main", "Main", "index.html")
                .Capability(new("patterns", ["fs:allow-watch", "fs:scope"], Windows: ["main"],
                    PermissionScopes: [new("fs:scope", [JsonValue.From(root), JsonValue.From(root + "/*/*.txt")], [JsonValue.From(nested)])])).Build();
            patternApp.Run();
            await GuestWatch(patternRuntime, Args([root], "{\"recursive\":true}"));
            var matched = Path.Combine(nested, "allowed.txt"); File.WriteAllText(matched, "matched descendant");
            await Until(patternRuntime, e => Paths(e).Contains(matched));
            await Drain(patternRuntime);
            File.WriteAllText(Path.Combine(nested, "ignored.bin"), "unmatched descendant"); await Silent(patternRuntime);
        }
        finally { Directory.Delete(root, true); }
    }

    internal static async Task ValidationAndDisposal()
    {
        var root = Directory.CreateTempSubdirectory("netwiv-watch-").FullName;
        try
        {
            var plugin = new FsPlugin(); var runtime = new FakeRuntime();
            using var app = new Builder().UseRuntime(runtime).Plugin(plugin).Build(); app.Run();
            foreach (var options in new[] { "{\"delayMs\":-1}", "{\"delayMs\":0.5}", "{\"delayMs\":\"bad\"}", "{\"recursive\":1}" })
                await Reject(() => app.Handle.InvokeAsync("plugin:fs|watch", Args([root], options)).AsTask());
            await Reject(() => app.Handle.InvokeAsync("plugin:fs|watch", Args([])).AsTask());
            await Reject(() => app.Handle.InvokeAsync("plugin:fs|watch", Args([root, Path.Combine(root, "missing")])).AsTask());
            await Drain(runtime);
            using var stop = new CancellationTokenSource(); stop.Cancel();
            try { await app.Handle.InvokeAsync("plugin:fs|watch", Args([root]), stop.Token); throw new Exception("Canceled watch succeeded."); }
            catch (OperationCanceledException) { }
            await app.Handle.InvokeAsync("plugin:fs|watch", Args([root]));
            plugin.Dispose(); await Drain(runtime);
            File.WriteAllText(Path.Combine(root, "after-dispose.txt"), "ignored"); await Silent(runtime);
            await Reject(() => app.Handle.InvokeAsync("plugin:fs|watch", Args([root])).AsTask());
            await Drain(runtime);
            var otherRuntime = new FakeRuntime();
            using var otherApp = new Builder().UseRuntime(otherRuntime).Plugin(new FsPlugin()).Build(); otherApp.Run();
            var rid = (await otherApp.Handle.InvokeAsync("plugin:fs|watch", Args([root]))).GetUInt32();
            otherApp.Dispose();
            Assert.True(otherApp.Handle.Resources.TryGet(rid) is null, "App disposal retained a watch.");
            File.WriteAllText(Path.Combine(root, "after-app-dispose.txt"), "ignored"); await Silent(otherRuntime);
        }
        finally { Directory.Delete(root, true); }
    }

    internal static async Task SymlinkScope()
    {
        var root = Directory.CreateTempSubdirectory("netwiv-watch-").FullName;
        try
        {
            var allowed = Directory.CreateDirectory(Path.Combine(root, "allowed")).FullName;
            var outside = Directory.CreateDirectory(Path.Combine(root, "outside")).FullName;
            var link = Path.Combine(allowed, "link"); Directory.CreateSymbolicLink(link, outside);
            var runtime = new FakeRuntime();
            using var app = Scoped(runtime, new FsPlugin(), allowed, []); app.Run();
            var rejected = await GuestIpc.Invoke(runtime, "plugin:fs|watch", Args([link]).GetRawText());
            Assert.Equal("error", rejected.Headers!["Tauri-Response"]);
            await GuestWatch(runtime, Args([allowed], "{\"recursive\":true,\"delayMs\":250}"));
            File.WriteAllText(Path.Combine(outside, "hidden.txt"), "hidden"); await Silent(runtime, 400);
            var descendant = Directory.CreateDirectory(Path.Combine(allowed, "descendant")).FullName;
            await Drain(runtime);
            File.WriteAllText(Path.Combine(descendant, "pending.txt"), "pending");
            Directory.Move(descendant, Path.Combine(outside, "moved"));
            Directory.CreateSymbolicLink(descendant, Path.Combine(outside, "moved"));
            await Task.Delay(400);
            while (runtime.PendingScripts > 0)
            {
                var value = Parse(await runtime.NextScript());
                if (value.TryGetProperty("message", out var message))
                    Assert.True(!Paths(message).Any(p => p.Contains("pending.txt", StringComparison.Ordinal)), "Queued event escaped through a retargeted ancestor.");
            }
        }
        finally { Directory.Delete(root, true); }
    }

    internal static async Task MovedRootScope()
    {
        foreach (var watchAncestor in new[] { false, true })
        foreach (var moveAncestor in new[] { false, true })
        foreach (var replace in new[] { false, true })
        foreach (var delay in new[] { 0, 250 })
        {
            if (watchAncestor && moveAncestor) continue;
            var directory = Directory.CreateTempSubdirectory("netwiv-watch-move-").FullName;
            try
            {
                var ancestor = Directory.CreateDirectory(Path.Combine(directory, "allowed")).FullName;
                var root = Directory.CreateDirectory(Path.Combine(ancestor, "root")).FullName;
                var outside = Directory.CreateDirectory(Path.Combine(directory, "denied")).FullName;
                var runtime = new FakeRuntime();
                var watched = watchAncestor ? ancestor : root;
                using var app = Scoped(runtime, new FsPlugin(), watched, []); app.Run();
                var rid = await GuestWatch(runtime, Args([watched], $"{{\"recursive\":true,\"delayMs\":{delay}}}"));
                var control = Path.Combine(root, "control.txt"); File.WriteAllText(control, "control");
                await Until(runtime, e => Paths(e).Contains(control)); await Drain(runtime);
                if (delay > 0)
                {
                    File.WriteAllText(Path.Combine(root, "authorized-before-move.txt"), "queued");
                    await Task.Delay(75);
                }
                var moved = Path.Combine(outside, "moved");
                if (OperatingSystem.IsWindows())
                {
                    var rejected = false;
                    try { Directory.Move(moveAncestor ? ancestor : root, moved); }
                    catch (IOException) { rejected = true; }
                    Assert.True(rejected, "Windows moved a directory held by an active watch guard.");
                    app.Handle.Resources.Close(rid); await Drain(runtime);
                    Directory.Move(moveAncestor ? ancestor : root, moved);
                    File.WriteAllText(Path.Combine(moveAncestor ? Path.Combine(moved, "root") : moved, "secret-name.txt"), "hidden");
                    await Silent(runtime);
                    continue;
                }
                Directory.Move(moveAncestor ? ancestor : root, moved);
                if (replace) Directory.CreateDirectory(root);
                File.WriteAllText(Path.Combine(moveAncestor ? Path.Combine(moved, "root") : moved, "secret-name.txt"), "hidden");
                await Task.Delay(450);
                var rescans = 0;
                while (runtime.PendingScripts > 0)
                {
                    var value = Parse(await runtime.NextScript());
                    if (value.TryGetProperty("message", out var message))
                    {
                        if (message.GetProperty("attrs").TryGetProperty("rescan", out _)) rescans++;
                        if (watchAncestor)
                            Assert.True(!message.GetProperty("attrs").TryGetProperty("error", out _),
                                $"Descendant move terminated the watcher (replacement={replace}, delay={delay}).");
                        var leaked = Paths(message).Select(Path.GetFileName).Where(name => name == "secret-name.txt").ToArray();
                        Assert.True(leaked.Length == 0,
                            $"A moved root disclosed {string.Join(',', leaked)} (descendant={watchAncestor}, ancestor={moveAncestor}, replacement={replace}, delay={delay}).");
                    }
                }
                if (watchAncestor)
                {
                    // The root remains authorized after its descendant leaves.
                    var retained = Path.Combine(ancestor, "retained.txt"); File.WriteAllText(retained, "visible");
                    try { await Until(runtime, e => Paths(e).Contains(retained)); }
                    catch (TimeoutException error)
                    {
                        throw new TimeoutException($"Watch did not deliver an allowed mutation after descendant movement (replacement={replace}, delay={delay}, rescans={rescans}).", error);
                    }
                }
            }
            finally { Directory.Delete(directory, true); }
        }
    }

    internal static async Task RearmDebounce()
    {
        var root = Directory.CreateTempSubdirectory("netwiv-watch-rearm-").FullName;
        try
        {
            var nested = Directory.CreateDirectory(Path.Combine(root, "nested")).FullName;
            var file = Path.Combine(nested, "existing.txt"); File.WriteAllText(file, "initial");
            var runtime = new FakeRuntime();
            using var app = new Builder().UseRuntime(runtime).Plugin(new FsPlugin()).Build(); app.Run();
            await app.Handle.InvokeAsync("plugin:fs|watch", Args([root], "{\"recursive\":true,\"delayMs\":300}"));
            File.AppendAllText(file, "old batch");
            await Task.Delay(200);
            Directory.Move(nested, Path.Combine(root, "renamed"));
            await Until(runtime, e => e.GetProperty("attrs").TryGetProperty("rescan", out var value) && value.GetBoolean());
            var elapsed = Stopwatch.StartNew();
            var fresh = Path.Combine(root, "fresh.txt"); File.WriteAllText(fresh, "new batch");
            await Until(runtime, e => Paths(e).Contains(fresh));
            Assert.True(elapsed.ElapsedMilliseconds >= 250, "A retired batch timer flushed the replacement generation early.");
        }
        finally { Directory.Delete(root, true); }
    }

    internal static async Task RearmLifetime()
    {
        var root = Directory.CreateTempSubdirectory("netwiv-watch-close-").FullName;
        try
        {
            var first = Directory.CreateDirectory(Path.Combine(root, "first")).FullName;
            var second = Directory.CreateDirectory(Path.Combine(root, "second")).FullName;
            var runtime = new FakeRuntime();
            using var app = new Builder().UseRuntime(runtime).Plugin(new FsPlugin()).Build(); app.Run();
            var rid = (await app.Handle.InvokeAsync("plugin:fs|watch", Args([first, second], "{\"recursive\":true}"))).GetUInt32();
            var control = Path.Combine(second, "control.txt"); File.WriteAllText(control, "control");
            await Until(runtime, e => Paths(e).Contains(control)); await Drain(runtime);
            var mutations = Task.Run(async () =>
            {
                for (var i = 0; i < 60; i++)
                {
                    var nested = Directory.CreateDirectory(Path.Combine(first, "change-" + i)).FullName;
                    File.WriteAllText(Path.Combine(nested, "change.txt"), "change");
                    File.AppendAllText(control, "active");
                    await Task.Delay(2);
                }
            });
            await Task.Delay(25);
            await Task.Run(() => app.Handle.Resources.Close(rid)).WaitAsync(TimeSpan.FromSeconds(8));
            await mutations.WaitAsync(TimeSpan.FromSeconds(8));
            await Drain(runtime);
            Assert.True(app.Handle.Resources.TryGet(rid) is null, "Concurrent rearm retained a closed resource.");
            File.AppendAllText(control, "after close"); await Silent(runtime);
        }
        finally { Directory.Delete(root, true); }
    }

    internal static async Task RecursiveRegistrationLimit()
    {
        var root = Directory.CreateTempSubdirectory("netwiv-watch-limit-").FullName;
        var incoming = Directory.CreateTempSubdirectory("netwiv-watch-incoming-").FullName;
        try
        {
            // The root plus its children require 129 native directory registrations.
            for (var i = 0; i < 128; i++) Directory.CreateDirectory(Path.Combine(root, "child-" + i));
            var runtime = new FakeRuntime();
            using var app = new Builder().UseRuntime(runtime).Plugin(new FsPlugin()).Build(); app.Run();
            await Reject(() => app.Handle.InvokeAsync("plugin:fs|watch", Args(Directory.GetDirectories(root).Prepend(root).ToArray())).AsTask());
            await Reject(() => app.Handle.InvokeAsync("plugin:fs|watch", Args([root], "{\"recursive\":true}")).AsTask());
            await Drain(runtime);
            // OS descriptor/watcher quotas may reject before the framework cap.
            // Cleanup must still allow a fresh, narrow registration.
            Directory.Delete(root, true); Directory.CreateDirectory(root);
            var rid = (await app.Handle.InvokeAsync("plugin:fs|watch", Args([root], "{\"recursive\":true}"))).GetUInt32();
            // One incoming directory event discovers the entire populated tree;
            // this does not require delivery of 128 separate native events.
            for (var i = 0; i < 128; i++) Directory.CreateDirectory(Path.Combine(incoming, "child-" + i));
            Directory.Move(incoming, Path.Combine(root, "incoming"));
            await Until(runtime, e => e.GetProperty("attrs").TryGetProperty("error", out var error) && error.GetString() == "watch-failed");
            await Drain(runtime);
            app.Handle.Resources.Close(rid);
            File.WriteAllText(Path.Combine(root, "after-limit.txt"), "closed"); await Silent(runtime);
        }
        finally { Directory.Delete(root, true); if (Directory.Exists(incoming)) Directory.Delete(incoming, true); }
    }

    private static App Scoped(FakeRuntime runtime, FsPlugin plugin, string root, JsonElement[] denied) =>
        new Builder().UseRuntime(runtime).Plugin(plugin).Window("main", "Main", "index.html").Window("other", "Other", "index.html")
            .Capability(new("watch", ["fs:allow-watch", "fs:scope", "core:resources:allow-close"], Windows: ["main", "other"],
            PermissionScopes: [new("fs:scope", [JsonValue.From(root), JsonValue.From(root + "/**")], denied)])).Build();

    private static JsonElement Args(string[] paths, string options = "{}") => JsonValue.Parse("{\"paths\":[" +
        string.Join(',', paths.Select(p => JsonValue.From(p).GetRawText())) + "],\"options\":" + options + ",\"onEvent\":\"__CHANNEL__:42\"}");

    private static async Task<uint> GuestWatch(FakeRuntime runtime, JsonElement args, ulong document = 0)
    {
        var response = await GuestIpc.Invoke(runtime, "plugin:fs|watch", args.GetRawText(), document);
        Assert.Equal("ok", response.Headers!["Tauri-Response"]);
        return JsonValue.Parse(GuestIpc.Body(response)).GetUInt32();
    }

    private static async Task Reject(Func<Task> operation)
    {
        try { await operation(); throw new Exception("Invalid watch registration succeeded."); }
        catch (InvokeException) { }
    }

    private static bool Kind(JsonElement value, string kind) => value.GetProperty("type").ValueKind == JsonValueKind.Object && value.GetProperty("type").TryGetProperty(kind, out _);
    private static string[] Paths(JsonElement value) => value.GetProperty("paths").EnumerateArray().Select(p => p.GetString()!).ToArray();
    private static JsonElement Parse(string script) => JsonValue.Parse(script[(script.IndexOf(',', StringComparison.Ordinal) + 1)..^2]);
    private static async Task<JsonElement> Until(FakeRuntime runtime, Func<JsonElement, bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (true)
        {
            var value = Parse(await runtime.NextScript(TimeSpan.FromSeconds(8)).WaitAsync(timeout.Token));
            if (value.TryGetProperty("message", out var message) && predicate(message)) return message;
        }
    }
    private static async Task Drain(FakeRuntime runtime)
    {
        await Task.Delay(100);
        while (runtime.PendingScripts > 0) await runtime.NextScript();
    }
    private static async Task Silent(FakeRuntime runtime, int milliseconds = 250)
    {
        await Task.Delay(milliseconds); Assert.Equal(0, runtime.PendingScripts);
    }
}
