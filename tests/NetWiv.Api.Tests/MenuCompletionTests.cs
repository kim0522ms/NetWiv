using System.Text.Json;
using NetWiv.Runtime;
using Xunit;

namespace NetWiv.Api.Tests;

public sealed class MenuCompletionTests
{
    [Fact]
    public async Task IconMenuItemUsesPinnedKindsAndSupportsImageNativeAndNullIcons()
    {
        var runtime = new RecordingRuntime();
        runtime.Enqueue("plugin:image|new", "41");
        runtime.Enqueue("plugin:menu|new", """[7,"open"]""");
        using var app = new Builder().UseRuntime(runtime).Build();
        var image = await Image.NewAsync(app.Handle, new byte[] { 1, 2, 3, 255 }, 1, 1);

        var item = await IconMenuItem.NewAsync(app.Handle,
            new IconMenuItemOptions("Open", "open", false, "Ctrl+O", image));

        Assert.Equal("open", item.Id);
        Assert.Equal("Icon", item.Kind);
        AssertCall(runtime, "plugin:menu|new",
            """{"kind":"Icon","options":{"id":"open","text":"Open","enabled":false,"accelerator":"Ctrl+O","icon":$ICON$},"handler":null}"""
                .Replace("$ICON$", image.ResourceId.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        await item.SetIconAsync(NativeIcon.Folder);
        AssertCall(runtime, "plugin:menu|set_icon", """{"rid":7,"kind":"Icon","icon":"Folder"}""");

        await item.SetIconAsync(null);
        AssertCall(runtime, "plugin:menu|set_icon", """{"rid":7,"kind":"Icon","icon":null}""");
    }

    [Fact]
    public async Task AboutPredefinedItemWritesExplicitCamelCaseMetadataOrNull()
    {
        var runtime = new RecordingRuntime();
        runtime.Enqueue("plugin:image|new", "42");
        runtime.Enqueue("plugin:menu|new", """[8,"about"]""");
        runtime.Enqueue("plugin:menu|new", """[9,"about-default"]""");
        using var app = new Builder().UseRuntime(runtime).Build();
        var image = await Image.NewAsync(app.Handle, new byte[] { 4, 5, 6, 255 }, 1, 1);
        var metadata = new AboutMetadata(
            Name: "NetWiv",
            Version: "2.11.5",
            ShortVersion: "2.11",
            Authors: ["Alice", "Bob"],
            Comments: "Desktop runtime",
            Copyright: "Copyright 2026",
            License: "MIT OR Apache-2.0",
            Website: "https://example.invalid",
            WebsiteLabel: "Project site",
            Credits: "Contributors",
            Icon: image);

        await PredefinedMenuItem.NewAsync(app.Handle,
            new PredefinedMenuItemOptions(PredefinedMenuItemType.About, "About NetWiv", metadata));

        AssertCall(runtime, "plugin:menu|new",
            """{"kind":"Predefined","options":{"text":"About NetWiv","item":{"About":{"name":"NetWiv","version":"2.11.5","shortVersion":"2.11","authors":["Alice","Bob"],"comments":"Desktop runtime","copyright":"Copyright 2026","license":"MIT OR Apache-2.0","website":"https://example.invalid","websiteLabel":"Project site","credits":"Contributors","icon":$ICON$}}},"handler":null}"""
                .Replace("$ICON$", image.ResourceId.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        await PredefinedMenuItem.NewAsync(app.Handle,
            new PredefinedMenuItemOptions(PredefinedMenuItemType.About));
        AssertCall(runtime, "plugin:menu|new",
            """{"kind":"Predefined","options":{"item":{"About":null}},"handler":null}""");
    }

    [Fact]
    public async Task MenuLookupsMaterializeIconItemsAndPreserveFutureKinds()
    {
        var runtime = new RecordingRuntime();
        runtime.Enqueue("plugin:menu|new", """[1,"root"]""");
        runtime.Enqueue("plugin:menu|get", """[11,"icon","Icon"]""");
        runtime.Enqueue("plugin:menu|items", """[[12,"future","FutureKind"]]""");
        using var app = new Builder().UseRuntime(runtime).Build();
        var menu = await Menu.NewAsync(app.Handle);

        var icon = Assert.IsType<IconMenuItem>(await menu.GetAsync("icon"));
        Assert.Equal("Icon", icon.Kind);
        var future = Assert.IsType<MenuItem>(Assert.Single(await menu.ItemsAsync()));
        Assert.Equal("FutureKind", future.Kind);
    }

    private static void AssertCall(RecordingRuntime runtime, string command, string expectedJson)
    {
        Assert.Equal(command, runtime.LastCommand);
        Assert.True(JsonElement.DeepEquals(JsonValue.Parse(expectedJson), runtime.LastArguments),
            $"Expected {expectedJson}, actual {runtime.LastArguments.GetRawText()}");
    }

    private sealed class RecordingRuntime : IDesktopRuntime
    {
        private readonly Dictionary<string, Queue<JsonElement>> _responses = new(StringComparer.Ordinal);

        public string? LastCommand { get; private set; }
        public JsonElement LastArguments { get; private set; }

        public void Enqueue(string command, string json)
        {
            if (!_responses.TryGetValue(command, out var queue))
            {
                queue = new Queue<JsonElement>();
                _responses.Add(command, queue);
            }
            queue.Enqueue(JsonValue.Parse(json));
        }

        public void Run(RuntimeContext context) { }
        public void Dispatch(Action action) => action();
        public void EvaluateScript(string webviewLabel, string script) { }
        public void Exit(int exitCode = 0) { }

        public Task<JsonElement> InvokeAsync(
            string command,
            JsonElement args,
            CancellationToken cancellationToken = default)
        {
            LastCommand = command;
            LastArguments = args.Clone();
            return Task.FromResult(_responses.TryGetValue(command, out var queue) && queue.Count > 0
                ? queue.Dequeue().Clone()
                : JsonValue.Null);
        }
    }
}
