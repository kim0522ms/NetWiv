// Copyright 2019-2024 Tauri Programme within The Commons Conservancy
// SPDX-License-Identifier: Apache-2.0 OR MIT
// Upstream merge_patches vectors: pinned Tauri CLI 2.11.4.
using System.Text.Json.Nodes;
using NetWiv.Build.Configuration;

internal static class ConfigOverlayTests
{
    internal static Task UpstreamMergePatches()
    {
        JsonNode? value = new JsonObject();
        foreach (var patch in new[] {
            """{"app":{"withGlobalTauri":true,"windows":[]},"plugins":{"test":"tauri"},"build":{"devUrl":"http://localhost:8080"}}""",
            """{"app":{"withGlobalTauri":null}}""",
            """{"app":{"windows":null}}""",
            """{"plugins":{"updater":{"endpoints":["https://tauri.app"]}}}""" })
            value = JsonMergePatch.Compose(value, JsonNode.Parse(patch));
        Equal("""
            {"app":{"withGlobalTauri":null,"windows":null},"plugins":{"test":"tauri",
            "updater":{"endpoints":["https://tauri.app"]}},"build":{"devUrl":"http://localhost:8080"}}
            """, value);
        return Task.CompletedTask;
    }

    internal static Task CompositionBoundaries()
    {
        var root = Path.Combine(Path.GetTempPath(), "netwiv-patch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "netwiv.conf.json"), """
                {"identifier":"com.test.patch","plugins":{"retained":true,"changed":"base"},
                 "build":{"devUrl":"http://localhost:8080","features":["base"]}}
                """);
            File.WriteAllText(Path.Combine(root, "netwiv.linux.conf.json"), """{"plugins":{"platform":true}}""");
            var first = Path.Combine(root, "overlay.json5");
            File.WriteAllText(first, "{ plugins: null, build: null }");
            var config = ConfigLoader.Load(root, "linux", [first,
                """{"plugins":{"changed":"cli"},"build":{"features":["cli"]}}"""]);
            // Tauri composes CLI patches first, then applies the composition once.
            Equal("""{"retained":true,"changed":"cli","platform":true}""", config.Node("plugins"));
            Equal("""{"devUrl":"http://localhost:8080","features":["cli"]}""", config.Node("build"));
            if (!config.Sources.Contains(first)) throw new Exception("Overlay file must remain a watched source.");
            Equal("""{"plugins":{"changed":"cli"},"build":{"features":["cli"]}}""", config.CliPatch);
            var removed = ConfigLoader.Load(root, "linux", ["""{"plugins":{"changed":null}}"""]);
            Equal("""{"retained":true,"platform":true}""", removed.Node("plugins"));
            Equal("""{"plugins":{"changed":null}}""", removed.CliPatch);
        }
        finally { Directory.Delete(root, true); }
        return Task.CompletedTask;
    }

    private static void Equal(string expected, JsonNode? actual)
    {
        if (!JsonNode.DeepEquals(JsonNode.Parse(expected), actual))
            throw new Exception($"Expected {expected}, got {actual}.");
    }
}
