using System.Text.Json.Nodes;
using System.Xml.Linq;
using NetWiv.Build.Configuration;
using NetWiv.Build.Execution;
using NetWiv.Cli;

internal static class MacDeepLinkPackagingTests
{
    internal static Task Configuration()
    {
        var root = Directory.CreateTempSubdirectory("netwiv-mac-url-types-").FullName;
        try
        {
            var config = new ConfigDocument(Path.Combine(root, "netwiv.conf.json"), root, root,
                JsonNode.Parse("""{"identifier":"dev.netwiv.url","productName":"URL Fixture","version":"1.0.0","plugins":{"deep-link":{}}}""")!.AsObject(), []);
            var plugin = config.Root["plugins"]!["deep-link"]!.AsObject();
            Check(Types(config) is null);
            plugin["desktop"] = JsonNode.Parse("""{"schemes":["Example","app+test","a.b-c"]}""");
            var single = Types(config)!;
            Check(single.Elements("dict").Count() == 1);
            Check(single.Descendants("string").Select(value => value.Value).SequenceEqual(new[] { "dev.netwiv.url", "Editor", "example", "app+test", "a.b-c" }));
            plugin["desktop"] = JsonNode.Parse("""[{"schemes":["one"]},{"schemes":["two"]}]""");
            Check(Types(config)!.Elements("dict").Count() == 2);
            Check(Types(config)!.Elements("dict").All(value => MacPropertyList.Get(value, "CFBundleTypeRole")?.Value == "Editor"));
            foreach (var malformed in new[] { "null", "false", "[null]", "{}", "{\"schemes\":null}", "{\"schemes\":[1]}", "{\"schemes\":[\"\"]}", "{\"schemes\":[\"1invalid\"]}", "{\"schemes\":[\"app://\"]}", "{\"schemes\":[\"white space\"]}" })
            {
                plugin["desktop"] = JsonNode.Parse(malformed);
                Throws(() => NativeBundler.ValidateConfiguration(config, BuildTarget.Resolve("osx-arm64"), "app", true));
            }
            File.WriteAllText(Path.Combine(root, "extra.plist"), """<plist version="1.0"><dict><key>CFBundleURLTypes</key><array><dict><key>CFBundleURLSchemes</key><array><string>manual</string></array></dict></array></dict></plist>""");
            config.Root["bundle"] = JsonNode.Parse("""{"macOS":{"infoPlist":"extra.plist"}}""");
            plugin.Remove("desktop");
            Check(Types(config)!.Descendants("string").Single().Value == "manual");
            plugin["desktop"] = JsonNode.Parse("""{"schemes":["configured"]}""");
            Throws(() => NativeBundler.MacInfoPlist(config, "App", "12.0"));
        }
        finally { Directory.Delete(root, true); }
        return Task.CompletedTask;
    }

    private static XElement? Types(ConfigDocument config) => MacPropertyList.Get(NativeBundler.MacInfoPlist(config, "App", "12.0").Root!.Element("dict")!, "CFBundleURLTypes");
    private static void Check(bool condition) { if (!condition) throw new Exception("macOS URL declaration assertion failed."); }
    private static void Throws(Action action) { try { action(); } catch (NetWivConfigException) { return; } throw new Exception("Expected invalid deep link configuration to fail."); }
}
