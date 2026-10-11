using System.Runtime.InteropServices.Marshalling;

namespace NetWiv.Platform.Windows.WebView2;

[GeneratedComClass]
internal sealed partial class EnvironmentCompletedHandler(
    Func<int, ICoreWebView2Environment?, int> callback) : IEnvironmentCompletedHandler
{
    public int Invoke(int errorCode, ICoreWebView2Environment? environment) => callback(errorCode, environment);
}

[GeneratedComClass]
internal sealed partial class ControllerCompletedHandler(
    Func<int, ICoreWebView2Controller?, int> callback) : IControllerCompletedHandler
{
    public int Invoke(int errorCode, ICoreWebView2Controller? controller) => callback(errorCode, controller);
}

[GeneratedComClass]
internal sealed partial class AddScriptCompletedHandler(
    Func<int, nint, int> callback) : IAddScriptCompletedHandler
{
    public int Invoke(int errorCode, nint scriptId) => callback(errorCode, scriptId);
}

[GeneratedComClass]
internal sealed partial class ExecuteScriptCompletedHandler(
    Func<int, nint, int> callback) : IExecuteScriptCompletedHandler
{
    public int Invoke(int errorCode, nint resultJson) => callback(errorCode, resultJson);
}

[GeneratedComClass]
internal sealed partial class WebMessageReceivedHandler(
    Func<ICoreWebView2, ICoreWebView2WebMessageReceivedEventArgs, int> callback) : IWebMessageReceivedHandler
{
    public int Invoke(ICoreWebView2 sender, ICoreWebView2WebMessageReceivedEventArgs args) => callback(sender, args);
}

[GeneratedComClass]
internal sealed partial class WebResourceRequestedHandler(
    Func<ICoreWebView2, ICoreWebView2WebResourceRequestedEventArgs, int> callback) : IWebResourceRequestedHandler
{
    public int Invoke(ICoreWebView2 sender, ICoreWebView2WebResourceRequestedEventArgs args) => callback(sender, args);
}

[GeneratedComClass]
internal sealed partial class NewBrowserVersionAvailableHandler(Func<nint, nint, int> callback)
    : INewBrowserVersionAvailableHandler
{
    public int Invoke(nint sender, nint args) => callback(sender, args);
}
