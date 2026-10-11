// Copyright 2019-2024 Tauri Programme within The Commons Conservancy
// SPDX-License-Identifier: Apache-2.0 OR MIT
using NetWiv.Build.Configuration;

internal static class WindowsFileVersionTests
{
    internal static Task NumericMetadata() => Check("1.2.3+42", 42);
    internal static Task CompositeMetadata() => Check("1.2.3+42.sha", 0);
    internal static Task TextMetadata() => Check("1.2.3+abc", 0);
    internal static Task LargeMetadata() => Check("1.2.3+70000", 0);
    internal static Task ConfigResolution()
    {
        var root = Path.Combine(Path.GetTempPath(), "netwiv-version-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(root, "src-netwiv");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "netwiv.conf.json"), """{"identifier":"test.netwiv.version","version":"../package.json"}""");
            var package = Path.Combine(root, "package.json");
            File.WriteAllText(package, """{"version":"1.2.3+42"}""");
            var config = ConfigLoader.Load(root, "windows");
            if (config.String("version") != "1.2.3+42" || !config.Sources.Contains(package))
                throw new Exception("Package version did not resolve or was omitted from development watch inputs.");
            File.WriteAllText(package, """{"version":"1.2.4-beta.1"}""");
            if (ConfigLoader.Load(root, "windows").String("version") != "1.2.4-beta.1") throw new Exception("Stale package version.");
            foreach (var text in new[] { "{}", "[]", "{\"version\":42}", "{\"version\":\"1.2.3-01\"}", "{\"version\":\"\"}" })
            {
                File.WriteAllText(package, text);
                try { ConfigLoader.Load(root, "windows"); }
                catch (NetWivConfigException) { continue; }
                throw new Exception("Invalid package version was accepted: " + text);
            }
            return Task.CompletedTask;
        }
        finally { Directory.Delete(root, true); }
    }
    private static Task Check(string version, ulong build)
    {
        var expected = (1UL << 48) | (2UL << 32) | (3UL << 16) | build;
        if (WindowsFileVersion.Encode(version) != expected || WindowsFileVersion.Format(version) != $"1.2.3.{build}")
            throw new Exception("Windows resource version mismatch: " + version);
        return Task.CompletedTask;
    }
}
