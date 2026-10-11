using System.Text.Json;

namespace NetWiv;

public partial class Window
{
    internal Window(AppHandle app, string label) { App = app; Label = label; }
    protected AppHandle App { get; }
    public string Label { get; }
    public Task<JsonElement> InvokeAsync(string command, JsonElement args, CancellationToken cancellationToken = default) =>
        App.InvokeForTargetAsync(command, args, Label, cancellationToken);
    public Task CloseAsync(CancellationToken cancellationToken = default) =>
        InvokeAsync("plugin:window|close", LabelArgument(), cancellationToken);
    public Task ShowAsync(CancellationToken cancellationToken = default) =>
        InvokeAsync("plugin:window|show", LabelArgument(), cancellationToken);
    public Task HideAsync(CancellationToken cancellationToken = default) =>
        InvokeAsync("plugin:window|hide", LabelArgument(), cancellationToken);

    private JsonElement LabelArgument() => JsonValue.Write(writer =>
    {
        writer.WriteStartObject(); writer.WriteString("label", Label); writer.WriteEndObject();
    });
}

public partial class Webview
{
    internal Webview(AppHandle app, string label, string? windowLabel = null)
    {
        App = app; Label = label; ParentWindowLabel = windowLabel ?? label;
    }
    protected AppHandle App { get; }
    public string Label { get; }
    internal string ParentWindowLabel { get; set; }
    public Window Window => new(App, App.WebviewParentLabel(Label) ?? ParentWindowLabel);
    public void EvaluateScript(string script) => App.EvaluateScript(Label, script);
    public Task<JsonElement> InvokeAsync(string command, JsonElement args, CancellationToken cancellationToken = default) =>
        App.InvokeForTargetAsync(command, args, Label, cancellationToken);
}

public sealed class WebviewWindow : Window
{
    internal WebviewWindow(AppHandle app, string label) : base(app, label) => Webview = new Webview(app, label);
    internal WebviewWindow(AppHandle app, string label, Webview webview) : base(app, label) => Webview = webview;
    public Webview Webview { get; }
    public void EvaluateScript(string script) => Webview.EvaluateScript(script);
    public Task<PhysicalPosition> PositionAsync(CancellationToken cancellationToken = default) => Webview.PositionAsync(cancellationToken);
    public Task<PhysicalSize> SizeAsync(CancellationToken cancellationToken = default) => Webview.SizeAsync(cancellationToken);
    public Task SetAutoResizeAsync(bool value, CancellationToken cancellationToken = default) => Webview.SetAutoResizeAsync(value, cancellationToken);
    public Task SetZoomAsync(double scaleFactor, CancellationToken cancellationToken = default) => Webview.SetZoomAsync(scaleFactor, cancellationToken);
    public Task ReparentAsync(string windowLabel, CancellationToken cancellationToken = default) => Webview.ReparentAsync(windowLabel, cancellationToken);
    public Task ClearAllBrowsingDataAsync(CancellationToken cancellationToken = default) => Webview.ClearAllBrowsingDataAsync(cancellationToken);
    public override async Task SetBackgroundColorAsync(RgbaColor? color, CancellationToken cancellationToken = default)
    {
        await base.SetBackgroundColorAsync(color, cancellationToken).ConfigureAwait(false);
        await Webview.SetBackgroundColorAsync(color, cancellationToken).ConfigureAwait(false);
    }
}
