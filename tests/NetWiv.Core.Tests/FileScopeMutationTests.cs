// Copyright 2019-2024 Tauri Programme within The Commons Conservancy
// SPDX-License-Identifier: Apache-2.0
// SPDX-License-Identifier: MIT
using NetWiv;
using NetWiv.Runtime;

internal static class FileScopeMutationTests
{
    private static string Root => OperatingSystem.IsWindows() ? @"C:\home\tauri" : "/home/tauri";
    private static FileScope NewScope() => new(default, "dev.netwiv.scope");

    public static Task PathIsEscaped()
    {
        var wildcard = Path.Combine(Root, "**");
        var nested = Path.Combine(wildcard, "file");
        var other = Path.Combine(Root, "anyfile");
        var scope = NewScope();
        scope.AllowDirectory(wildcard, false);
        Assert.True(scope.IsAllowed(wildcard) && scope.IsAllowed(nested), "Literal wildcard directory was not allowed.");
        Assert.True(!scope.IsAllowed(other), "Directory wildcard was interpreted as a glob.");
        scope = NewScope();
        scope.AllowFile(wildcard);
        Assert.True(scope.IsAllowed(wildcard), "Literal wildcard file was not allowed.");
        Assert.True(!scope.IsAllowed(nested) && !scope.IsAllowed(other), "File wildcard escaped its literal boundary.");
        scope = NewScope();
        scope.AllowDirectory(Root, true);
        scope.ForbidDirectory(wildcard, false);
        Assert.True(!scope.IsAllowed(wildcard) && !scope.IsAllowed(nested), "Literal forbidden directory was not enforced.");
        Assert.True(scope.IsAllowed(other), "Forbidden literal wildcard became a broad deny.");
        Assert.True(scope.IsAllowed(Path.Combine(nested, "inner")), "Nonrecursive directory deny rejected a deeper descendant.");
        Assert.True(scope.IsAllowed(Path.Combine(Root, "inner", "folder", "anyfile")), "Literal deny rejected a different descendant.");
        scope = NewScope();
        scope.AllowDirectory(Root, true);
        scope.ForbidFile(wildcard);
        Assert.True(!scope.IsAllowed(wildcard), "Literal forbidden file was not enforced.");
        Assert.True(scope.IsAllowed(nested) && scope.IsAllowed(other), "Forbidden literal file denied descendants.");
        Assert.True(scope.IsAllowed(Path.Combine(nested, "inner")), "Literal file deny rejected a deep descendant.");
        scope = NewScope();
        scope.AllowDirectory(Root, false);
        Assert.True(scope.IsAllowed(wildcard) && scope.IsAllowed(other), "Nonrecursive allow omitted direct children.");
        Assert.True(!scope.IsAllowed(nested) && !scope.IsAllowed(Path.Combine(nested, "inner")), "Nonrecursive allow admitted descendants.");
        return Task.CompletedTask;
    }

    public static Task WindowsRootPaths()
    {
        var unc = NewScope();
        unc.AllowDirectory(@"\\localhost\c$", true);
        foreach (var path in new[] { @"\\localhost\c$", @"\\localhost\c$\Windows", @"\\localhost\c$\NonExistentFile" })
            Assert.True(unc.IsAllowed(path), $"UNC path rejected: {path}");
        foreach (var path in new[] { @"\\localhost\d$", @"\\OtherServer\Share" })
            Assert.True(!unc.IsAllowed(path), $"Different UNC path allowed: {path}");
        unc = NewScope();
        unc.AllowDirectory(@"\\?\UNC\localhost\c$", true);
        foreach (var path in new[] { @"\\localhost\c$", @"\\localhost\c$\Windows", @"\\?\UNC\localhost\c$\Windows\NonExistentFile" })
            Assert.True(unc.IsAllowed(path), $"Verbatim UNC path rejected: {path}");
        foreach (var path in new[] { @"\\localhost\c$\Windows\NonExistentFile", @"\\localhost\d$", @"\\OtherServer\Share" })
            Assert.True(!unc.IsAllowed(path), $"Verbatim UNC path unexpectedly matched: {path}");
        var device = NewScope();
        device.AllowFile(@"\\.\COM1");
        Assert.True(device.IsAllowed(@"\\.\COM1") && !device.IsAllowed(@"\\.\COM2"), "Device namespace matching differs.");
        foreach (var root in new[] { @"C:\", @"\\?\C:\" })
        {
            var scope = NewScope();
            scope.AllowDirectory(root, true);
            Assert.True(scope.IsAllowed(@"C:\Windows"), "Windows directory was rejected.");
            Assert.True(scope.IsAllowed(@"C:\Windows\system.ini"), "Windows file was rejected.");
            Assert.True(scope.IsAllowed(@"C:\NonExistentFile"), "Nonexistent file under allowed drive was rejected.");
            Assert.True(!scope.IsAllowed(@"D:\home"), "A different drive was allowed.");
        }
        var verbatim = NewScope();
        verbatim.AllowFile(@"\\?\anyfile");
        Assert.True(verbatim.IsAllowed(@"\\?\anyfile"), "Verbatim file was rejected.");
        Assert.True(!verbatim.IsAllowed(@"\\?\otherfile"), "Different verbatim file was allowed.");
        return Task.CompletedTask;
    }

    public static Task PushPatternGeneratedPaths()
    {
        var rawDirectory = OperatingSystem.IsWindows() ? @"C:\path\to\dir" : "/path/to/dir/";
        var rawChild = OperatingSystem.IsWindows() ? @"C:\path\to\dir\**" : "/path/to/dir/**";
        var configured = new FileScope(JsonValue.Parse("[" + JsonValue.From(rawDirectory).GetRawText() + "," + JsonValue.From(rawChild).GetRawText() + "]"), "dev.netwiv.scope");
        foreach (var expected in OperatingSystem.IsWindows()
            ? new[] { @"C:\path\to\dir", @"C:\path\to\dir\**", @"\\?\C:\path\to\dir", @"\\?\C:\path\to\dir\**" }
            : new[] { "/path/to/dir", "/path/to/dir/**" })
            Assert.True(configured.AllowedPatterns.Contains(expected), $"Raw generated pattern is absent: {expected}");
        var scope = NewScope();
        var directory = OperatingSystem.IsWindows() ? @"C:\path\to\dir" : "/path/to/dir/";
        scope.AllowDirectory(directory, true);
        var normalized = directory.TrimEnd(Path.DirectorySeparatorChar);
        Assert.True(scope.AllowedPatterns.Contains(normalized), "Directory pattern was not normalized.");
        Assert.True(scope.AllowedPatterns.Contains(Path.Combine(normalized, "**")), "Recursive child pattern is absent.");
        if (OperatingSystem.IsWindows())
        {
            // The literal ? in the canonical Win32 prefix is escaped by allow_directory.
            Assert.True(scope.AllowedPatterns.Contains(@"\\[?]\C:\path\to\dir"), "Canonical Windows pattern is absent.");
            Assert.True(scope.AllowedPatterns.Contains(@"\\[?]\C:\path\to\dir\**"), "Canonical recursive Windows pattern is absent.");
        }
        return Task.CompletedTask;
    }

    public static async Task EventNoDeadlocks()
    {
        var scope = NewScope();
        var calls = 0;
        scope.Once(value =>
        {
            Assert.Equal(FileScopeEventKind.PathAllowed, value.Kind);
            calls++;
            scope.AllowFile(Path.Combine(Root, "another-test-path"));
        });
        await Task.Run(() => scope.AllowFile(Path.Combine(Root, "test-path"))).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, calls);
    }

    public static Task ConcurrentChangesAndDenyPrecedence()
    {
        var scope = NewScope();
        var events = 0;
        var listener = scope.Listen(_ => Interlocked.Increment(ref events));
        Parallel.For(0, 100, index => scope.AllowFile(Path.Combine(Root, "file" + index)));
        Assert.Equal(100, events);
        scope.Unlisten(listener);
        var file = Path.Combine(Root, "file50");
        scope.ForbidFile(file);
        scope.AllowFile(file);
        Assert.True(scope.IsForbidden(file) && !scope.IsAllowed(file), "Later allow defeated a deny.");
        Assert.Equal(100, events);
        var snapshot = scope.AllowedPatterns;
        scope.AllowFile(Path.Combine(Root, "new"));
        Assert.True(!snapshot.Contains(Path.Combine(Root, "new")), "Pattern snapshot changed after publication.");
        return Task.CompletedTask;
    }

    public static Task AssetProtocolUsesLiveScope()
    {
        var path = Path.Combine(Path.GetTempPath(), "netwiv-live-scope-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "live scope");
        try
        {
            var runtime = new FakeRuntime();
            using var app = new Builder().UseRuntime(runtime)
                .UseEmbeddedConfig(assembly: typeof(FileScopeMutationTests).Assembly, resourceName: "asset-config.json").Build();
            app.Run();
            var scope = app.AssetProtocolScope();
            var request = new AssetRequest(new Uri("asset://localhost/" + Uri.EscapeDataString(path)), "GET", new Dictionary<string, string>());
            Assert.Equal(403, runtime.Context!.ResolveAsset(request)!.StatusCode);
            scope.AllowFile(path);
            Assert.Equal(200, runtime.Context.ResolveAsset(request)!.StatusCode);
            app.Handle.AssetProtocolScope().ForbidFile(path);
            Assert.Equal(403, runtime.Context.ResolveAsset(request)!.StatusCode);
        }
        finally { File.Delete(path); }
        return Task.CompletedTask;
    }
}
