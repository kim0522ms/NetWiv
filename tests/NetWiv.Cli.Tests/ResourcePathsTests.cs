// Assertions ported from pinned Tauri tauri-utils/resources.rs and bundler/utils/mod.rs.
// Copyright 2019-2024 Tauri Programme within The Commons Conservancy.
// SPDX-License-Identifier: Apache-2.0 OR MIT
using System.Text.Json;
using NetWiv.Build.Packaging;
using NetWiv.Build.Configuration;
using NetWiv.Build.Execution;
using NetWiv.Cli;

internal static class ResourcePathsTests
{
    internal static async Task BundleRefresh()
    {
        var root = Path.Combine(Path.GetTempPath(), "netwiv-resource-bundle-" + Guid.NewGuid().ToString("N"));
        var backend = Path.Combine(root, "src-netwiv");
        var output = Path.Combine(root, "target", BuildTarget.Resolve(null).RequestedTarget, "release");
        Directory.CreateDirectory(backend);
        Directory.CreateDirectory(output);
        try
        {
            var config = Path.Combine(backend, "netwiv.conf.json");
            void Configure(string resource) => File.WriteAllText(config,
                """{"identifier":"test.netwiv.bundle","build":{"dotnetProject":"Probe.csproj"},"bundle":{"targets":[],"resources":RESOURCE}}""".Replace("RESOURCE", resource, StringComparison.Ordinal));
            File.WriteAllText(Path.Combine(backend, "Probe.csproj"), "<Project />");
            var binary = Path.Combine(output, "Probe" + (OperatingSystem.IsWindows() ? ".exe" : ""));
            File.WriteAllText(binary, "unchanged executable");
            File.WriteAllText(Path.Combine(root, "source.txt"), "one");
            Configure("""{"../source.txt":"first.txt"}""");
            var error = new StringWriter();
            async Task<int> Bundle() => await CliApp.RunAsync(["bundle", "--no-sign"], TextWriter.Null, error, root);
            if (await Bundle() != 0) throw new Exception(error.ToString());
            Equal("one", File.ReadAllText(Path.Combine(output, "first.txt")));
            File.WriteAllText(Path.Combine(root, "source.txt"), "two");
            Configure("""{"../source.txt":"second.txt"}""");
            if (await Bundle() != 0) throw new Exception(error.ToString());
            Equal("two", File.ReadAllText(Path.Combine(output, "second.txt")));
            if (File.Exists(Path.Combine(output, "first.txt"))) throw new Exception("Standalone bundle retained a stale resource.");
            Configure("""["../missing.txt"]""");
            if (await Bundle() != 1 || !error.ToString().Contains("Bundle resource expansion failed", StringComparison.Ordinal))
                throw new Exception("Missing bundle resource did not produce a CLI diagnostic and exit code 1.");
            Equal("unchanged executable", File.ReadAllText(binary));
            Equal("two", File.ReadAllText(Path.Combine(output, "second.txt")));
        }
        finally { Directory.Delete(root, true); }
    }

    internal static async Task Watching()
    {
        var root = Path.Combine(Path.GetTempPath(), "netwiv-resource-watch-" + Guid.NewGuid().ToString("N"));
        var backend = Path.Combine(root, "src-netwiv");
        var external = Path.Combine(root, "external;assets");
        Directory.CreateDirectory(backend);
        Directory.CreateDirectory(external);
        try
        {
            File.WriteAllText(Path.Combine(backend, "netwiv.conf.json"), """{"identifier":"test.netwiv.resources","bundle":{"resources":{"../external;assets/*.txt":"data"}}}""");
            var config = ConfigLoader.Load(root, OperatingSystem.IsWindows() ? "windows" : "linux");
            foreach (var aot in new[] { false, true })
            {
                using var watcher = aot ? DevCommand.CreateWatcher(config, Path.Combine(backend, "App.csproj"), [], false) : DevCommand.WatchConfiguration(config);
                File.WriteAllText(Path.Combine(external, "ignored.bin"), "ignored");
                using (var quiet = new CancellationTokenSource(350))
                {
                    try { await watcher.WaitAsync(quiet.Token); throw new Exception("Unselected external file triggered restart."); }
                    catch (OperationCanceledException) when (quiet.IsCancellationRequested) { }
                }
                var resource = Path.Combine(external, aot ? "aot.txt" : "managed.txt");
                File.WriteAllText(resource, "new resource");
                await watcher.WaitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
                await Task.Delay(250);
                watcher.Drain();
                File.Delete(resource);
                await watcher.WaitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        finally { Directory.Delete(root, true); }
    }

    internal static Task RelativePaths()
    {
        Equal("data/images/button.png", ResourcePaths.RelativePath("./data/images/button.png"));
        Equal("_up_/_up_/images/wheel.png", ResourcePaths.RelativePath("../../images/wheel.png"));
        Equal("_root_/home/ferris/crab.png", ResourcePaths.RelativePath("/home/ferris/crab.png"));
        return Task.CompletedTask;
    }

    internal static Task Run(string name)
    {
        using var fixture = JsonDocument.Parse(typeof(ResourcePathsTests).Assembly.GetManifestResourceStream("upstream-resources.json")!);
        var test = fixture.RootElement.GetProperty("cases").EnumerateArray().Single(value => value.GetProperty("name").GetString() == name);
        var root = Path.Combine(Path.GetTempPath(), "netwiv-resources-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (var file in fixture.RootElement.GetProperty("files").EnumerateArray())
            {
                var path = Path.Combine(root, file.GetString()!);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, file.GetString());
            }
            Directory.CreateDirectory(Path.Combine(root, "empty-directory"));
            var result = ResourcePaths.Expand(test.GetProperty("input"), Path.Combine(root, "src-tauri"), test.GetProperty("allowWalk").GetBoolean());
            var expected = test.GetProperty("expected").EnumerateArray().Select(pair => pair[0].GetString() + " -> " + pair[1].GetString()).Order(StringComparer.Ordinal).ToArray();
            var actual = result.Files.Select(file => Slash(file.SourcePath) + " -> " + Slash(file.TargetPath)).Order(StringComparer.Ordinal).ToArray();
            if (!expected.SequenceEqual(actual)) throw new Exception($"{name}: expected {string.Join(';', expected)}, got {string.Join(';', actual)}; errors: {string.Join(';', result.Errors)}");
            if (test.GetProperty("errorsOnly").GetBoolean())
            {
                if (result.Errors.Count != 5 || result.Errors.Count(error => error is FileNotFoundException && error.Message.StartsWith("Resource path", StringComparison.Ordinal)) != 2 ||
                    result.Errors.Count(error => error is InvalidOperationException) != 1 ||
                    result.Errors.Count(error => error is FileNotFoundException && error.Message.StartsWith("Glob pattern", StringComparison.Ordinal)) != 2)
                    throw new Exception("Resource failure classes differ from the five upstream cases: " + string.Join(';', result.Errors));
            }
        }
        finally { Directory.Delete(root, true); }
        return Task.CompletedTask;
    }

    internal static Task Boundaries()
    {
        var root = Path.Combine(Path.GetTempPath(), "netwiv-resource-boundaries-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "a.txt"), "a");
            File.WriteAllText(Path.Combine(root, "b.txt"), "b");
            File.WriteAllText(Path.Combine(root, ".hidden.txt"), "hidden");
            using var mapped = JsonDocument.Parse("""{"a.txt":"./folder/","b.txt":"../outside","*.txt":"flatten"}""");
            var result = ResourcePaths.Expand(mapped.RootElement, root);
            if (result.Errors.Count != 0 || result.Files.Count != 5) throw new Exception("Resource boundary expansion failed.");
            Equal("folder", result.Files[0].TargetPath); // A mapped literal file destination is exact, even with a trailing slash.
            Equal("_up_/outside", result.Files[1].TargetPath);
            if (!result.Files.Any(file => file.TargetPath == Path.Combine("flatten", ".hidden.txt"))) throw new Exception("Default glob must include dot files.");
            using var invalid = JsonDocument.Parse("""["***.txt"]""");
            if (ResourcePaths.Expand(invalid.RootElement, root).Errors.Single() is not ArgumentException)
                throw new Exception("Invalid glob was not rejected.");
            using var wrong = JsonDocument.Parse("""{"a.txt":42}""");
            try { ResourcePaths.Expand(wrong.RootElement, root); throw new Exception("Non-string destination accepted."); }
            catch (ArgumentException) { }
        }
        finally { Directory.Delete(root, true); }
        return Task.CompletedTask;
    }

    internal static Task Staging()
    {
        var root = Path.Combine(Path.GetTempPath(), "netwiv-resource-stage-" + Guid.NewGuid().ToString("N"));
        var output = Path.Combine(root, "publish");
        Directory.CreateDirectory(output);
        try
        {
            File.WriteAllText(Path.Combine(root, "source.txt"), "before");
            File.WriteAllText(Path.Combine(output, "app.exe"), "unrelated");
            ResourceStager.Stage([new("source.txt", "data/readme.txt")], root, output + Path.DirectorySeparatorChar);
            Equal("before", File.ReadAllText(Path.Combine(output, "data/readme.txt")));
            File.WriteAllText(Path.Combine(root, "source.txt"), "after change");
            ResourceStager.Stage([new("source.txt", "data/readme.txt")], root, output);
            Equal("after change", File.ReadAllText(Path.Combine(output, "data/readme.txt")));
            ResourceStager.Stage([new("source.txt", "renamed.txt")], root, output);
            if (File.Exists(Path.Combine(output, "data/readme.txt"))) throw new Exception("Stale resource survived a target rename.");
            ResourceStager.Stage([], root, output);
            if (File.Exists(Path.Combine(output, "renamed.txt"))) throw new Exception("Removed resource survived restaging.");
            Equal("unrelated", File.ReadAllText(Path.Combine(output, "app.exe")));
            if (Directory.EnumerateFiles(output, "*.json", SearchOption.AllDirectories).Any()) throw new Exception("Resource staging metadata leaked into the payload.");
            try { ResourceStager.Stage([new("source.txt", "new.txt"), new("source.txt", "../escaped.txt")], root, output); throw new Exception("Escaping destination accepted."); }
            catch (IOException) { }
            if (File.Exists(Path.Combine(output, "new.txt")) || File.Exists(Path.Combine(root, "escaped.txt"))) throw new Exception("Failed resource plan performed partial writes.");
            if (!OperatingSystem.IsWindows())
            {
                var outside = Path.Combine(root, "outside");
                Directory.CreateDirectory(outside);
                Directory.CreateSymbolicLink(Path.Combine(output, "link"), outside);
                try { ResourceStager.Stage([new("source.txt", "link/escaped.txt")], root, output); throw new Exception("Symlink destination accepted."); }
                catch (IOException) { }
                if (File.Exists(Path.Combine(outside, "escaped.txt"))) throw new Exception("Resource write escaped through a directory link.");
            }
        }
        finally { Directory.Delete(root, true); }
        return Task.CompletedTask;
    }

    private static string Slash(string path) => path.Replace('\\', '/');
    private static void Equal(string expected, string actual)
    {
        if (expected != Slash(actual)) throw new Exception($"Expected '{expected}', got '{actual}'.");
    }
}
