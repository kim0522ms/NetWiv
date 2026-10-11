using NetWiv.Build.Configuration;

internal static class BundleTargetTests
{
    internal static Task ValidateTargets()
    {
        var root = Directory.CreateTempSubdirectory("netwiv-bundle-targets-").FullName;
        try
        {
            foreach (var targets in new[] { "[\"nsis\",\"msi\"]", "[\"deb\",\"rpm\",\"appimage\"]", "[]", "\"all\"", "\"nsis\"", "null" })
            {
                Write(targets);
                _ = ConfigLoader.Load(root, "windows");
            }
            foreach (var targets in new[] { "true", "42", "{}", "\"unknown\"", "[\"nsis\",false]", "[\"unknown\"]" })
            {
                Write(targets);
                try { _ = ConfigLoader.Load(root, "windows"); }
                catch (NetWivConfigException) { continue; }
                throw new Exception("Invalid bundle targets were accepted: " + targets);
            }
            void Write(string targets) => File.WriteAllText(Path.Combine(root, "netwiv.conf.json"),
                "{\"identifier\":\"dev.netwiv.targets\",\"bundle\":{\"targets\":" + targets + "}}");
        }
        finally { Directory.Delete(root, true); }
        return Task.CompletedTask;
    }
}
