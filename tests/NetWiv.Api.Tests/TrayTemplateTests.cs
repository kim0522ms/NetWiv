using System.Text.Json;
using NetWiv.Runtime;
using Xunit;

namespace NetWiv.Api.Tests;

public sealed class TrayTemplateTests
{
    [Fact]
    public async Task CreationRetainsExplicitTemplateAndLeftClickFlags()
    {
        var runtime = new RecordingRuntime();
        using var app = new Builder().UseRuntime(runtime).Build();
        await TrayIcon.NewAsync(app.Handle, new TrayIconOptions(
            Id: "tray-probe", IconAsTemplate: false, ShowMenuOnLeftClick: false));
        AssertCall(runtime, "plugin:tray|new",
            """{"options":{"id":"tray-probe","iconAsTemplate":false,"showMenuOnLeftClick":false},"handler":null}""");
    }

    [Fact]
    public async Task TemplateMutationPreservesNullableImageAndFalseValues()
    {
        var runtime = new RecordingRuntime();
        using var app = new Builder().UseRuntime(runtime).Build();
        var image = await Image.NewAsync(app.Handle, new byte[] { 230, 30, 30, 255 }, 1, 1);
        var tray = await TrayIcon.NewAsync(app.Handle, new TrayIconOptions(Icon: image, IconAsTemplate: true));

        await tray.SetIconWithTemplateAsync(image, true);
        AssertCall(runtime, "plugin:tray|set_icon_with_as_template",
            """{"rid":7,"icon":$ICON$,"asTemplate":true}"""
                .Replace("$ICON$", image.ResourceId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        await tray.SetIconAsTemplateAsync(false);
        AssertCall(runtime, "plugin:tray|set_icon_as_template",
            """{"rid":7,"asTemplate":false}""");
        await tray.SetIconWithTemplateAsync(null, false);
        AssertCall(runtime, "plugin:tray|set_icon_with_as_template",
            """{"rid":7,"icon":null,"asTemplate":false}""");
        await tray.SetShowMenuOnLeftClickAsync(false);
        AssertCall(runtime, "plugin:tray|set_show_menu_on_left_click",
            """{"rid":7,"onLeft":false}""");
        await tray.SetShowMenuOnLeftClickAsync(true);
        AssertCall(runtime, "plugin:tray|set_show_menu_on_left_click",
            """{"rid":7,"onLeft":true}""");
    }

    private static void AssertCall(RecordingRuntime runtime, string command, string expected)
    {
        Assert.Equal(command, runtime.Command);
        Assert.True(JsonElement.DeepEquals(JsonValue.Parse(expected), runtime.Arguments),
            $"Expected {expected}, actual {runtime.Arguments.GetRawText()}");
    }

    private sealed class RecordingRuntime : IDesktopRuntime
    {
        internal string? Command { get; private set; }
        internal JsonElement Arguments { get; private set; }
        public void Run(RuntimeContext context) { }
        public void Dispatch(Action action) => action();
        public void EvaluateScript(string webviewLabel, string script) { }
        public void Exit(int exitCode = 0) { }
        public Task<JsonElement> InvokeAsync(string command, JsonElement args, CancellationToken cancellationToken = default)
        {
            Command = command;
            Arguments = args.Clone();
            return Task.FromResult(command switch
            {
                "plugin:image|new" => JsonValue.Parse("41"),
                "plugin:tray|new" => JsonValue.Parse("""[7,"tray-probe"]"""),
                _ => JsonValue.Null,
            });
        }
    }
}
