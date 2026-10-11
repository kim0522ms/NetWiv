using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using NetWiv.Platform.Windows.Interop;

namespace NetWiv.Platform.Windows.WebView2;

internal enum WebResourceContext
{
    All = 0,
}

[GeneratedComInterface, Guid("4E8A3389-C9D8-4BD2-B6B5-124FEE6CC14D")]
internal partial interface IEnvironmentCompletedHandler
{
    [PreserveSig]
    int Invoke(int errorCode, ICoreWebView2Environment? environment);
}

[GeneratedComInterface, Guid("6C4819F3-C9B7-4260-8127-C9F5BDE7F68C")]
internal partial interface IControllerCompletedHandler
{
    [PreserveSig]
    int Invoke(int errorCode, ICoreWebView2Controller? controller);
}

[GeneratedComInterface, Guid("B99369F3-9B11-47B5-BC6F-8E7895FCEA17")]
internal partial interface IAddScriptCompletedHandler
{
    [PreserveSig]
    int Invoke(int errorCode, nint scriptId);
}

[GeneratedComInterface, Guid("49511172-CC67-4BCA-9923-137112F4C4CC")]
internal partial interface IExecuteScriptCompletedHandler
{
    [PreserveSig]
    int Invoke(int errorCode, nint resultJson);
}

[GeneratedComInterface, Guid("57213F19-00E6-49FA-8E07-898EA01ECBD2")]
internal partial interface IWebMessageReceivedHandler
{
    [PreserveSig]
    int Invoke(ICoreWebView2 sender, ICoreWebView2WebMessageReceivedEventArgs args);
}

[GeneratedComInterface, Guid("AB00B74C-15F1-4646-80E8-E76341D25D71")]
internal partial interface IWebResourceRequestedHandler
{
    [PreserveSig]
    int Invoke(ICoreWebView2 sender, ICoreWebView2WebResourceRequestedEventArgs args);
}

[GeneratedComInterface, Guid("F9A2976E-D34E-44FC-ADEE-81B6B57CA914")]
internal partial interface INewBrowserVersionAvailableHandler
{
    [PreserveSig]
    int Invoke(nint sender, nint args);
}

[GeneratedComInterface, Guid("76ECEACB-0462-4D94-AC83-423A6793775E")]
internal partial interface ICoreWebView2
{
    [PreserveSig] int GetSettings(out nint settings);
    [PreserveSig] int GetSource(out nint uri);
    [PreserveSig] int Navigate([MarshalAs(UnmanagedType.LPWStr)] string uri);
    [PreserveSig] int NavigateToString([MarshalAs(UnmanagedType.LPWStr)] string htmlContent);
    [PreserveSig] int AddNavigationStarting(nint handler, out EventRegistrationToken token);
    [PreserveSig] int RemoveNavigationStarting(EventRegistrationToken token);
    [PreserveSig] int AddContentLoading(nint handler, out EventRegistrationToken token);
    [PreserveSig] int RemoveContentLoading(EventRegistrationToken token);
    [PreserveSig] int AddSourceChanged(nint handler, out EventRegistrationToken token);
    [PreserveSig] int RemoveSourceChanged(EventRegistrationToken token);
    [PreserveSig] int AddHistoryChanged(nint handler, out EventRegistrationToken token);
    [PreserveSig] int RemoveHistoryChanged(EventRegistrationToken token);
    [PreserveSig] int AddNavigationCompleted(nint handler, out EventRegistrationToken token);
    [PreserveSig] int RemoveNavigationCompleted(EventRegistrationToken token);
    [PreserveSig] int AddFrameNavigationStarting(nint handler, out EventRegistrationToken token);
    [PreserveSig] int RemoveFrameNavigationStarting(EventRegistrationToken token);
    [PreserveSig] int AddFrameNavigationCompleted(nint handler, out EventRegistrationToken token);
    [PreserveSig] int RemoveFrameNavigationCompleted(EventRegistrationToken token);
    [PreserveSig] int AddScriptDialogOpening(nint handler, out EventRegistrationToken token);
    [PreserveSig] int RemoveScriptDialogOpening(EventRegistrationToken token);
    [PreserveSig] int AddPermissionRequested(nint handler, out EventRegistrationToken token);
    [PreserveSig] int RemovePermissionRequested(EventRegistrationToken token);
    [PreserveSig] int AddProcessFailed(nint handler, out EventRegistrationToken token);
    [PreserveSig] int RemoveProcessFailed(EventRegistrationToken token);
    [PreserveSig] int AddScriptToExecuteOnDocumentCreated([MarshalAs(UnmanagedType.LPWStr)] string javaScript, IAddScriptCompletedHandler handler);
    [PreserveSig] int RemoveScriptToExecuteOnDocumentCreated([MarshalAs(UnmanagedType.LPWStr)] string id);
    [PreserveSig] int ExecuteScript([MarshalAs(UnmanagedType.LPWStr)] string javaScript, IExecuteScriptCompletedHandler? handler);
    [PreserveSig] int CapturePreview(int imageFormat, nint stream, nint handler);
    [PreserveSig] int Reload();
    [PreserveSig] int PostWebMessageAsJson([MarshalAs(UnmanagedType.LPWStr)] string json);
    [PreserveSig] int PostWebMessageAsString([MarshalAs(UnmanagedType.LPWStr)] string value);
    [PreserveSig] int AddWebMessageReceived(IWebMessageReceivedHandler handler, out EventRegistrationToken token);
    [PreserveSig] int RemoveWebMessageReceived(EventRegistrationToken token);
    [PreserveSig] int CallDevToolsProtocolMethod(nint methodName, nint parameters, nint handler);
    [PreserveSig] int GetBrowserProcessId(out uint value);
    [PreserveSig] int GetCanGoBack(out int value);
    [PreserveSig] int GetCanGoForward(out int value);
    [PreserveSig] int GoBack();
    [PreserveSig] int GoForward();
    [PreserveSig] int GetDevToolsProtocolEventReceiver(nint eventName, out nint receiver);
    [PreserveSig] int Stop();
    [PreserveSig] int AddNewWindowRequested(nint handler, out EventRegistrationToken token);
    [PreserveSig] int RemoveNewWindowRequested(EventRegistrationToken token);
    [PreserveSig] int AddDocumentTitleChanged(nint handler, out EventRegistrationToken token);
    [PreserveSig] int RemoveDocumentTitleChanged(EventRegistrationToken token);
    [PreserveSig] int GetDocumentTitle(out nint title);
    [PreserveSig] int AddHostObjectToScript(nint name, nint value);
    [PreserveSig] int RemoveHostObjectFromScript(nint name);
    [PreserveSig] int OpenDevToolsWindow();
    [PreserveSig] int AddContainsFullScreenElementChanged(nint handler, out EventRegistrationToken token);
    [PreserveSig] int RemoveContainsFullScreenElementChanged(EventRegistrationToken token);
    [PreserveSig] int GetContainsFullScreenElement(out int value);
    [PreserveSig] int AddWebResourceRequested(IWebResourceRequestedHandler handler, out EventRegistrationToken token);
    [PreserveSig] int RemoveWebResourceRequested(EventRegistrationToken token);
    [PreserveSig] int AddWebResourceRequestedFilter([MarshalAs(UnmanagedType.LPWStr)] string uri, WebResourceContext context);
    [PreserveSig] int RemoveWebResourceRequestedFilter([MarshalAs(UnmanagedType.LPWStr)] string uri, WebResourceContext context);
    [PreserveSig] int AddWindowCloseRequested(nint handler, out EventRegistrationToken token);
    [PreserveSig] int RemoveWindowCloseRequested(EventRegistrationToken token);
}

[GeneratedComInterface, Guid("4D00C0D1-9434-4EB6-8078-8697A560334F")]
internal partial interface ICoreWebView2Controller
{
    [PreserveSig] int GetIsVisible(out int value);
    [PreserveSig] int SetIsVisible(int value);
    [PreserveSig] int GetBounds(out Rect bounds);
    [PreserveSig] int SetBounds(Rect bounds);
    [PreserveSig] int GetZoomFactor(out double value);
    [PreserveSig] int SetZoomFactor(double value);
    [PreserveSig] int AddZoomFactorChanged(nint handler, out EventRegistrationToken token);
    [PreserveSig] int RemoveZoomFactorChanged(EventRegistrationToken token);
    [PreserveSig] int SetBoundsAndZoomFactor(Rect bounds, double zoomFactor);
    [PreserveSig] int MoveFocus(int reason);
    [PreserveSig] int AddMoveFocusRequested(nint handler, out EventRegistrationToken token);
    [PreserveSig] int RemoveMoveFocusRequested(EventRegistrationToken token);
    [PreserveSig] int AddGotFocus(nint handler, out EventRegistrationToken token);
    [PreserveSig] int RemoveGotFocus(EventRegistrationToken token);
    [PreserveSig] int AddLostFocus(nint handler, out EventRegistrationToken token);
    [PreserveSig] int RemoveLostFocus(EventRegistrationToken token);
    [PreserveSig] int AddAcceleratorKeyPressed(nint handler, out EventRegistrationToken token);
    [PreserveSig] int RemoveAcceleratorKeyPressed(EventRegistrationToken token);
    [PreserveSig] int GetParentWindow(out nint window);
    [PreserveSig] int SetParentWindow(nint window);
    [PreserveSig] int NotifyParentWindowPositionChanged();
    [PreserveSig] int Close();
    [PreserveSig] int GetCoreWebView2(out ICoreWebView2? coreWebView2);
}

[GeneratedComInterface, Guid("B96D755E-0319-4E92-A296-23436F46A1FC")]
internal partial interface ICoreWebView2Environment
{
    [PreserveSig] int CreateCoreWebView2Controller(nint parentWindow, IControllerCompletedHandler handler);
    [PreserveSig] int CreateWebResourceResponse(IComStream content, int statusCode, [MarshalAs(UnmanagedType.LPWStr)] string reasonPhrase, [MarshalAs(UnmanagedType.LPWStr)] string headers, out ICoreWebView2WebResourceResponse? response);
    [PreserveSig] int GetBrowserVersionString(out nint versionInfo);
    [PreserveSig] int AddNewBrowserVersionAvailable(INewBrowserVersionAvailableHandler handler, out EventRegistrationToken token);
    [PreserveSig] int RemoveNewBrowserVersionAvailable(EventRegistrationToken token);
}

[GeneratedComInterface, Guid("0F99A40C-E962-4207-9E92-E3D542EFF849")]
internal partial interface ICoreWebView2WebMessageReceivedEventArgs
{
    [PreserveSig] int GetSource(out nint value);
    [PreserveSig] int GetWebMessageAsJson(out nint value);
    [PreserveSig] int TryGetWebMessageAsString(out nint value);
}

[GeneratedComInterface, Guid("97055CD4-512C-4264-8B5F-E3F446CEA6A5")]
internal partial interface ICoreWebView2WebResourceRequest
{
    [PreserveSig] int GetUri(out nint uri);
    [PreserveSig] int SetUri(nint uri);
    [PreserveSig] int GetMethod(out nint method);
    [PreserveSig] int SetMethod(nint method);
    [PreserveSig] int GetContent(out nint content);
    [PreserveSig] int SetContent(nint content);
    [PreserveSig] int GetHeaders(out ICoreWebView2HttpRequestHeaders? headers);
}

[GeneratedComInterface, Guid("E86CAC0E-5523-465C-B536-8FB9FC8C8C60")]
internal partial interface ICoreWebView2HttpRequestHeaders
{
    [PreserveSig] int GetHeader([MarshalAs(UnmanagedType.LPWStr)] string name, out nint value);
    [PreserveSig] int GetHeaders([MarshalAs(UnmanagedType.LPWStr)] string name, out ICoreWebView2HttpHeadersCollectionIterator? value);
    [PreserveSig] int Contains([MarshalAs(UnmanagedType.LPWStr)] string name, out int value);
    [PreserveSig] int SetHeader([MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string value);
    [PreserveSig] int RemoveHeader([MarshalAs(UnmanagedType.LPWStr)] string name);
    [PreserveSig] int GetIterator(out ICoreWebView2HttpHeadersCollectionIterator? value);
}

[GeneratedComInterface, Guid("0702FC30-F43B-47BB-AB52-A42CB552AD9F")]
internal partial interface ICoreWebView2HttpHeadersCollectionIterator
{
    [PreserveSig] int GetCurrentHeader(out nint name, out nint value);
    [PreserveSig] int GetHasCurrentHeader(out int hasCurrent);
    [PreserveSig] int MoveNext(out int hasNext);
}

[GeneratedComInterface, Guid("453E667F-12C7-49D4-BE6D-DDBE7956F57A")]
internal partial interface ICoreWebView2WebResourceRequestedEventArgs
{
    [PreserveSig] int GetRequest(out ICoreWebView2WebResourceRequest? request);
    [PreserveSig] int GetResponse(out ICoreWebView2WebResourceResponse? response);
    [PreserveSig] int SetResponse(ICoreWebView2WebResourceResponse response);
    [PreserveSig] int GetDeferral(out nint deferral);
    [PreserveSig] int GetResourceContext(out WebResourceContext context);
}

[GeneratedComInterface, Guid("AAFCC94F-FA27-48FD-97DF-830EF75AAEC9")]
internal partial interface ICoreWebView2WebResourceResponse
{
    [PreserveSig] int GetContent(out nint content);
    [PreserveSig] int SetContent(nint content);
    [PreserveSig] int GetHeaders(out nint headers);
    [PreserveSig] int GetStatusCode(out int value);
    [PreserveSig] int SetStatusCode(int value);
    [PreserveSig] int GetReasonPhrase(out nint value);
    [PreserveSig] int SetReasonPhrase(nint value);
}

[GeneratedComInterface, Guid("0000000C-0000-0000-C000-000000000046")]
internal partial interface IComStream
{
    [PreserveSig] int Read(nint buffer, uint count, nint bytesRead);
    [PreserveSig] int Write(nint buffer, uint count, nint bytesWritten);
    [PreserveSig] int Seek(long move, uint origin, nint newPosition);
    [PreserveSig] int SetSize(ulong newSize);
    [PreserveSig] int CopyTo(IComStream stream, ulong count, nint bytesRead, nint bytesWritten);
    [PreserveSig] int Commit(uint flags);
    [PreserveSig] int Revert();
    [PreserveSig] int LockRegion(ulong offset, ulong count, uint type);
    [PreserveSig] int UnlockRegion(ulong offset, ulong count, uint type);
    [PreserveSig] int Stat(nint stat, uint flags);
    [PreserveSig] int Clone(out IComStream? stream);
}

internal static partial class WebView2Loader
{
    [LibraryImport("WebView2Loader.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int CreateCoreWebView2EnvironmentWithOptions(
        string? browserExecutableFolder,
        string? userDataFolder,
        nint environmentOptions,
        IEnvironmentCompletedHandler environmentCreatedHandler);
}
