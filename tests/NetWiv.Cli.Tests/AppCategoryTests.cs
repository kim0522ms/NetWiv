// Source vectors: pinned Tauri category.rs, MIT OR Apache-2.0; see fixture notice.
using System.Text.Json;
using System.Text.Json.Nodes;
using NetWiv.Build.Configuration;
using NetWiv.Build.Packaging;
using NetWiv.Cli;

internal static class AppCategoryTests
{
    private static JsonDocument Fixture() => JsonDocument.Parse(typeof(AppCategoryTests).Assembly.GetManifestResourceStream("upstream-categories.json")!);

    internal static Task Valid()
    {
        using var fixture = Fixture();
        foreach (var test in fixture.RootElement.GetProperty("ok").EnumerateArray())
            Equal(test.GetProperty("name").GetString(), AppCategory.Parse(test.GetProperty("input").GetString()!).Name);
        return Task.CompletedTask;
    }

    internal static Task Suggestions()
    {
        using var fixture = Fixture();
        foreach (var test in fixture.RootElement.GetProperty("suggestions").EnumerateArray())
        {
            if (AppCategory.TryParse(test.GetProperty("input").GetString()!, out var category, out var suggestion) || category is not null)
                throw new Exception("Misspelling must not be silently accepted.");
            Equal(test.GetProperty("suggestion").GetString(), suggestion);
        }
        return Task.CompletedTask;
    }

    internal static Task Wrong()
    {
        using var fixture = Fixture();
        foreach (var test in fixture.RootElement.GetProperty("wrong").EnumerateArray())
            if (AppCategory.TryParse(test.GetString()!, out var category, out var suggestion) || category is not null || suggestion is not null)
                throw new Exception("Unrelated input must not be accepted or suggested.");
        return Task.CompletedTask;
    }

    internal static Task RoundTrip()
    {
        using var fixture = Fixture();
        foreach (var test in fixture.RootElement.GetProperty("categories").EnumerateArray())
        {
            var value = test.GetProperty("macos").GetString()!;
            Equal(value, AppCategory.Parse(value).MacOSApplicationCategoryType);
        }
        return Task.CompletedTask;
    }

    internal static Task DesktopMetadata()
    {
        using var fixture = Fixture();
        foreach (var test in fixture.RootElement.GetProperty("aliases").EnumerateArray())
            Equal(test.GetProperty("name").GetString(), AppCategory.Parse(test.GetProperty("input").GetString()!).Name);
        var root = Path.Combine(Path.GetTempPath(), "netwiv-category-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            void Write(JsonNode? category) => File.WriteAllText(Path.Combine(root, "netwiv.conf.json"),
                new JsonObject { ["productName"] = "CategoryProbe", ["bundle"] = new JsonObject { ["category"] = category } }.ToJsonString());
            foreach (var test in fixture.RootElement.GetProperty("categories").EnumerateArray())
            {
                Write(JsonValue.Create(test.GetProperty("canonical").GetString()));
                var config = ConfigLoader.Load(root, "linux");
                var desktop = NativeBundler.DesktopEntry(config, "category-probe");
                if (!desktop.Contains("\nCategories=" + test.GetProperty("desktop").GetString() + "\n", StringComparison.Ordinal))
                    throw new Exception("Linux desktop metadata ignored the configured category: " + desktop);
            }
            foreach (var invalid in new JsonNode[] { JsonValue.Create(42)!, JsonValue.Create("strategery")!, JsonValue.Create("\tEducation")! })
            {
                Write(invalid);
                try { _ = ConfigLoader.Load(root, "linux"); throw new Exception("Invalid category accepted."); }
                catch (NetWivConfigException) { }
            }
            Write(null);
            if (!NativeBundler.DesktopEntry(ConfigLoader.Load(root, "linux"), "probe").Contains("Categories=\n", StringComparison.Ordinal))
                throw new Exception("Absent categories must match Tauri's empty desktop category list.");
        }
        finally { Directory.Delete(root, true); }
        return Task.CompletedTask;
    }

    private static void Equal(string? expected, string? actual)
    {
        if (expected != actual) throw new Exception($"Expected '{expected}', got '{actual}'.");
    }
}
