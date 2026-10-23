using System.Text.Json.Nodes;
using NetWiv.Build.Configuration;
using NetWiv.Build.Execution;
using NetWiv.Build.Packaging;

internal static class ExternalBinaryTests
{
    internal static Task ResolutionAndStaging()
    {
        var root = Directory.CreateTempSubdirectory("netwiv-external-bin-").FullName;
        try
        {
            var target = OperatingSystem.IsWindows()
                ? BuildTarget.Resolve("win-x64")
                : OperatingSystem.IsMacOS()
                    ? BuildTarget.Resolve("osx-arm64")
                    : BuildTarget.Resolve("linux-x64");
            var triple = target.Platform switch
            {
                "windows" => "x86_64-pc-windows-msvc",
                "macos" => "aarch64-apple-darwin",
                _ => "x86_64-unknown-linux-gnu"
            };
            var extension = target.Platform == "windows" ? ".exe" : string.Empty;
            Directory.CreateDirectory(Path.Combine(root, "binaries"));
            var sourcePath = Path.Combine(root, "binaries", "helper-" + triple + extension);
            File.WriteAllText(sourcePath, "helper contents");
            Directory.CreateDirectory(Path.Combine(root, "other"));
            File.WriteAllText(Path.Combine(root, "other", "helper-" + triple + extension), "another helper");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(sourcePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute);

            var config = Config(root, "[\"binaries/helper\"]");
            ExternalBinaryStager.ValidateConfiguration(config.Root);
            var plan = ExternalBinaryStager.Resolve(config, target, "app" + extension);
            Equal(1, plan.Count);
            Equal(sourcePath, plan[0].Source);
            Equal("helper" + extension, plan[0].DestinationName);

            var firstPublish = Path.Combine(root, "publish-1");
            var secondPublish = Path.Combine(root, "publish-2");
            Directory.CreateDirectory(firstPublish);
            Directory.CreateDirectory(secondPublish);
            ExternalBinaryStager.Stage(plan, firstPublish);
            ExternalBinaryStager.Stage(plan, secondPublish);
            var firstCopy = Path.Combine(firstPublish, "helper" + extension);
            Equal("helper contents", File.ReadAllText(firstCopy));
            if (!OperatingSystem.IsWindows()) Equal(File.GetUnixFileMode(sourcePath), File.GetUnixFileMode(firstCopy));

            File.SetLastWriteTimeUtc(firstCopy, DateTime.UtcNow.AddHours(-1));
            var preservedTime = File.GetLastWriteTimeUtc(firstCopy);
            ExternalBinaryStager.Stage(plan, firstPublish);
            Equal(preservedTime, File.GetLastWriteTimeUtc(firstCopy));
            if (!OperatingSystem.IsWindows())
            {
                var executableMode = File.GetUnixFileMode(firstCopy);
                File.SetUnixFileMode(firstCopy, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                Throws<IOException>(() => ExternalBinaryStager.Stage(plan, firstPublish));
                if (File.GetUnixFileMode(firstCopy) != (UnixFileMode.UserRead | UnixFileMode.UserWrite))
                    throw new Exception("An identical but non-executable existing file was modified.");
                File.SetUnixFileMode(firstCopy, executableMode);

                var sourceMode = File.GetUnixFileMode(sourcePath);
                File.SetUnixFileMode(sourcePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                Throws<NetWivConfigException>(() => ExternalBinaryStager.Resolve(config, target, "app"));
                File.SetUnixFileMode(sourcePath, sourceMode);
            }
            File.WriteAllText(firstCopy, "user content");
            Throws<IOException>(() => ExternalBinaryStager.Stage(plan, firstPublish));
            Equal("user content", File.ReadAllText(firstCopy));

            File.WriteAllText(sourcePath, "updated helper contents");
            Throws<IOException>(() => ExternalBinaryStager.Stage(plan, secondPublish));
            Equal("helper contents", File.ReadAllText(Path.Combine(secondPublish, "helper" + extension)));

            var secondSource = Path.Combine(root, "other", "second-" + triple + extension);
            File.WriteAllText(secondSource, "second helper");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(secondSource, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var twoBinaries = ExternalBinaryStager.Resolve(Config(root, "[\"binaries/helper\",\"other/second\"]"), target, "app" + extension);
            var preflight = Path.Combine(root, "preflight");
            Directory.CreateDirectory(preflight);
            File.WriteAllText(Path.Combine(preflight, "second" + extension), "occupied");
            Throws<IOException>(() => ExternalBinaryStager.Stage(twoBinaries, preflight));
            if (File.Exists(Path.Combine(preflight, "helper" + extension))) throw new Exception("A failed preflight copied an earlier sidecar.");

            var collision = Config(root, "[\"binaries/helper\",\"other/helper\"]");
            Throws<NetWivConfigException>(() => ExternalBinaryStager.Resolve(collision, target, "app" + extension));
            var appCollision = Config(root, "[\"binaries/app\"]");
            File.WriteAllText(Path.Combine(root, "binaries", "app-" + triple + extension), "app helper");
            Throws<NetWivConfigException>(() => ExternalBinaryStager.Resolve(appCollision, target, "app" + extension));

            foreach (var malformed in new[] { "[\"../helper\"]", "[\"/helper\"]", "[\"binaries/helper.exe\"]", "[1]", "{}", "null" })
            {
                var invalid = Config(root, malformed);
                Throws<NetWivConfigException>(() => ExternalBinaryStager.ValidateConfiguration(invalid.Root));
                Throws<NetWivConfigException>(() => ExternalBinaryStager.Resolve(invalid, target, "app" + extension));
            }
            Throws<NetWivConfigException>(() => ExternalBinaryStager.Resolve(Config(root, "[\"bad\\u0001name\"]"), target, "app" + extension));

            var missing = Config(root, "[\"binaries/missing\"]");
            Throws<FileNotFoundException>(() => ExternalBinaryStager.Resolve(missing, target, "app" + extension));

            if (target.Platform == "windows")
            {
                var caseCollision = Config(root, "[\"binaries/helper\",\"other/HELPER\"]");
                File.WriteAllText(Path.Combine(root, "other", "HELPER-" + triple + extension), "case collision");
                Throws<NetWivConfigException>(() => ExternalBinaryStager.Resolve(caseCollision, target, "app.exe"));
                Throws<NetWivConfigException>(() => ExternalBinaryStager.Resolve(Config(root, "[\"binaries/CON\"]"), target, "app.exe"));
                Throws<NetWivConfigException>(() => ExternalBinaryStager.Resolve(Config(root, "[\"binaries/UNINSTALL\"]"), target, "app.exe"));
            }
            var macHelper = Path.Combine(root, "binaries", "CaseHelper-aarch64-apple-darwin");
            File.WriteAllText(macHelper, "macOS case collision fixture");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(macHelper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var macCollision = Config(root, "[\"binaries/CaseHelper\",\"other/casehelper\"]");
            Throws<NetWivConfigException>(() => ExternalBinaryStager.Resolve(macCollision, BuildTarget.Resolve("osx-arm64"), "App"));
            Throws<NetWivConfigException>(() => ExternalBinaryStager.Resolve(Config(root, "[\"binaries/casehelper\"]"), BuildTarget.Resolve("osx-arm64"), "CaseHelper"));
        }
        finally { Directory.Delete(root, true); }
        return Task.CompletedTask;
    }

    internal static Task RejectsLinks()
    {
        var root = Directory.CreateTempSubdirectory("netwiv-external-bin-links-").FullName;
        try
        {
            var target = OperatingSystem.IsWindows() ? BuildTarget.Resolve("win-x64") : BuildTarget.Resolve("linux-x64");
            var triple = target.Platform == "windows" ? "x86_64-pc-windows-msvc" : "x86_64-unknown-linux-gnu";
            var extension = target.Platform == "windows" ? ".exe" : string.Empty;
            var sourceRoot = Path.Combine(root, "sources");
            Directory.CreateDirectory(sourceRoot);
            var outside = Path.Combine(root, "outside" + extension);
            File.WriteAllText(outside, "outside");
            var link = Path.Combine(sourceRoot, "helper-" + triple + extension);
            if (TryCreateLink(() => File.CreateSymbolicLink(link, outside), "source file link"))
                Throws<NetWivConfigException>(() => ExternalBinaryStager.Resolve(Config(root, "[\"sources/helper\"]"), target, "app" + extension));

            var outsideDirectory = Path.Combine(root, "outside-directory");
            Directory.CreateDirectory(outsideDirectory);
            File.WriteAllText(Path.Combine(outsideDirectory, "helper-" + triple + extension), "helper through linked parent");
            var parentLink = Path.Combine(sourceRoot, "linked-parent");
            if (TryCreateLink(() => Directory.CreateSymbolicLink(parentLink, outsideDirectory), "source parent directory link"))
                Throws<NetWivConfigException>(() => ExternalBinaryStager.Resolve(Config(root, "[\"sources/linked-parent/helper\"]"), target, "app" + extension));

            var regularRoot = Path.Combine(root, "regular");
            Directory.CreateDirectory(regularRoot);
            var regularSource = Path.Combine(regularRoot, "helper-" + triple + extension);
            File.WriteAllText(regularSource, "helper");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(regularSource, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var plan = ExternalBinaryStager.Resolve(Config(root, "[\"regular/helper\"]"), target, "app" + extension);
            var output = Path.Combine(root, "output");
            Directory.CreateDirectory(output);
            var outputDirectoryLink = Path.Combine(root, "output-directory-link");
            if (TryCreateLink(() => Directory.CreateSymbolicLink(outputDirectoryLink, output), "destination directory link"))
            {
                Throws<IOException>(() => ExternalBinaryStager.Stage(plan, outputDirectoryLink));
                ExternalBinaryStager.Stage([], outputDirectoryLink);
            }

            var outputLink = Path.Combine(output, "helper" + extension);
            if (TryCreateLink(() => File.CreateSymbolicLink(outputLink, Path.Combine(outsideDirectory, "target")), "destination file link"))
                Throws<IOException>(() => ExternalBinaryStager.Stage(plan, output));
        }
        finally { Directory.Delete(root, true); }
        return Task.CompletedTask;
    }

    private static ConfigDocument Config(string root, string externalBin) => new(
        Path.Combine(root, "netwiv.conf.json"), root, root,
        new JsonObject { ["bundle"] = new JsonObject { ["externalBin"] = JsonNode.Parse(externalBin) } }, []);

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected '{expected}', got '{actual}'.");
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }

    private static bool TryCreateLink(Action create, string fixture)
    {
        try { create(); return true; }
        catch (Exception exception) when (exception is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
        {
            Console.WriteLine($"SKIP {fixture}: symbolic links are unavailable ({exception.GetType().Name})");
            return false;
        }
    }
}
