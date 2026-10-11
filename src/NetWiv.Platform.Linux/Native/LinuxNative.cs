using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NetWiv.Platform.Linux.Native;

internal static unsafe partial class LinuxNative
{
    private const string Gtk = "libgtk-3.so.0";
    private const string Gdk = "libgdk-3.so.0";
    private const string GObject = "libgobject-2.0.so.0";
    private const string GLib = "libglib-2.0.so.0";
    private const string Gio = "libgio-2.0.so.0";
    private const string WebKit = "libwebkit2gtk-4.1.so.0";
    private const string JavaScriptCore = "libjavascriptcoregtk-4.1.so.0";
    private const string AppIndicator = "libayatana-appindicator3.so.1";
    private const string Soup = "libsoup-3.0.so.0";
    private const string GdkPixbuf = "libgdk_pixbuf-2.0.so.0";

    [LibraryImport(Gtk, EntryPoint = "gtk_init_check")]
    internal static partial int GtkInitCheck(nint argc, nint argv);

    [LibraryImport(Gtk, EntryPoint = "gtk_window_new")]
    internal static partial nint GtkWindowNew(int windowType);

    [LibraryImport(Gtk, EntryPoint = "gtk_window_set_title", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void GtkWindowSetTitle(nint window, string title);

    [LibraryImport(Gtk, EntryPoint = "gtk_window_set_default_size")]
    internal static partial void GtkWindowSetDefaultSize(nint window, int width, int height);

    [LibraryImport(Gtk, EntryPoint = "gtk_window_get_size")]
    internal static partial void GtkWindowGetSize(nint window, out int width, out int height);

    [LibraryImport(Gtk, EntryPoint = "gtk_window_resize")]
    internal static partial void GtkWindowResize(nint window, int width, int height);

    [LibraryImport(Gtk, EntryPoint = "gtk_window_get_position")]
    internal static partial void GtkWindowGetPosition(nint window, out int x, out int y);

    [LibraryImport(Gtk, EntryPoint = "gtk_window_move")]
    internal static partial void GtkWindowMove(nint window, int x, int y);

    [LibraryImport(Gtk, EntryPoint = "gtk_window_get_title")]
    internal static partial nint GtkWindowGetTitle(nint window);

    [LibraryImport(Gtk, EntryPoint = "gtk_window_fullscreen")]
    internal static partial void GtkWindowFullscreen(nint window);

    [LibraryImport(Gtk, EntryPoint = "gtk_window_unfullscreen")]
    internal static partial void GtkWindowUnfullscreen(nint window);

    [LibraryImport(Gtk, EntryPoint = "gtk_window_get_resizable")]
    internal static partial int GtkWindowGetResizable(nint window);

    [LibraryImport(Gtk, EntryPoint = "gtk_window_set_resizable")]
    internal static partial void GtkWindowSetResizable(nint window, int resizable);

    [LibraryImport(Gtk, EntryPoint = "gtk_window_get_decorated")]
    internal static partial int GtkWindowGetDecorated(nint window);

    [LibraryImport(Gtk, EntryPoint = "gtk_window_set_decorated")]
    internal static partial void GtkWindowSetDecorated(nint window, int decorated);

    [LibraryImport(Gtk, EntryPoint = "gtk_window_set_keep_above")]
    internal static partial void GtkWindowSetKeepAbove(nint window, int setting);

    [LibraryImport(Gtk, EntryPoint = "gtk_window_is_active")]
    internal static partial int GtkWindowIsActive(nint window);

    [LibraryImport(Gtk, EntryPoint = "gtk_container_add")]
    internal static partial void GtkContainerAdd(nint container, nint widget);

    [LibraryImport(Gtk, EntryPoint = "gtk_widget_reparent")]
    internal static partial void GtkWidgetReparent(nint widget, nint newParent);

    [LibraryImport(Gtk, EntryPoint = "gtk_box_new")]
    internal static partial nint GtkBoxNew(int orientation, int spacing);

    [LibraryImport(Gtk, EntryPoint = "gtk_box_pack_start")]
    internal static partial void GtkBoxPackStart(nint box, nint child, int expand, int fill, uint padding);

    [LibraryImport(Gtk, EntryPoint = "gtk_box_reorder_child")]
    internal static partial void GtkBoxReorderChild(nint box, nint child, int position);

    [LibraryImport(Gtk, EntryPoint = "gtk_menu_bar_new")]
    internal static partial nint GtkMenuBarNew();

    [LibraryImport(Gtk, EntryPoint = "gtk_menu_new")]
    internal static partial nint GtkMenuNew();

    [LibraryImport(Gtk, EntryPoint = "gtk_menu_item_new_with_label", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint GtkMenuItemNewWithLabel(string label);

    [LibraryImport(Gtk, EntryPoint = "gtk_image_menu_item_new_with_label", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint GtkImageMenuItemNewWithLabel(string label);

    [LibraryImport(Gtk, EntryPoint = "gtk_image_menu_item_set_image")]
    internal static partial void GtkImageMenuItemSetImage(nint menuItem, nint image);

    [LibraryImport(Gtk, EntryPoint = "gtk_image_new_from_pixbuf")]
    internal static partial nint GtkImageNewFromPixbuf(nint pixbuf);

    [LibraryImport(Gtk, EntryPoint = "gtk_check_menu_item_new_with_label", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint GtkCheckMenuItemNewWithLabel(string label);

    [LibraryImport(Gtk, EntryPoint = "gtk_check_menu_item_get_active")]
    internal static partial int GtkCheckMenuItemGetActive(nint menuItem);

    [LibraryImport(Gtk, EntryPoint = "gtk_check_menu_item_set_active")]
    internal static partial void GtkCheckMenuItemSetActive(nint menuItem, int isActive);

    [LibraryImport(Gtk, EntryPoint = "gtk_menu_item_get_label")]
    internal static partial nint GtkMenuItemGetLabel(nint menuItem);

    [LibraryImport(Gtk, EntryPoint = "gtk_menu_item_set_label", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void GtkMenuItemSetLabel(nint menuItem, string label);

    [LibraryImport(Gtk, EntryPoint = "gtk_separator_menu_item_new")]
    internal static partial nint GtkSeparatorMenuItemNew();

    [LibraryImport(Gtk, EntryPoint = "gtk_menu_item_set_submenu")]
    internal static partial void GtkMenuItemSetSubmenu(nint menuItem, nint submenu);

    [LibraryImport(Gtk, EntryPoint = "gtk_menu_shell_append")]
    internal static partial void GtkMenuShellAppend(nint menuShell, nint child);

    [LibraryImport(Gtk, EntryPoint = "gtk_menu_shell_prepend")]
    internal static partial void GtkMenuShellPrepend(nint menuShell, nint child);

    [LibraryImport(Gtk, EntryPoint = "gtk_menu_shell_insert")]
    internal static partial void GtkMenuShellInsert(nint menuShell, nint child, int position);

    [LibraryImport(Gtk, EntryPoint = "gtk_container_remove")]
    internal static partial void GtkContainerRemove(nint container, nint widget);

    [LibraryImport(Gtk, EntryPoint = "gtk_fixed_new")]
    internal static partial nint GtkFixedNew();

    [LibraryImport(Gtk, EntryPoint = "gtk_fixed_put")]
    internal static partial void GtkFixedPut(nint fixedContainer, nint widget, int x, int y);

    [LibraryImport(Gtk, EntryPoint = "gtk_fixed_move")]
    internal static partial void GtkFixedMove(nint fixedContainer, nint widget, int x, int y);

    [LibraryImport(Gtk, EntryPoint = "gtk_widget_set_size_request")]
    internal static partial void GtkWidgetSetSizeRequest(nint widget, int width, int height);

    [LibraryImport(Gtk, EntryPoint = "gtk_widget_show_all")]
    internal static partial void GtkWidgetShowAll(nint widget);

    [LibraryImport(Gtk, EntryPoint = "gtk_widget_show")]
    internal static partial void GtkWidgetShow(nint widget);

    [LibraryImport(Gtk, EntryPoint = "gtk_widget_hide")]
    internal static partial void GtkWidgetHide(nint widget);

    [LibraryImport(Gtk, EntryPoint = "gtk_widget_get_visible")]
    internal static partial int GtkWidgetGetVisible(nint widget);

    [LibraryImport(Gtk, EntryPoint = "gtk_widget_get_sensitive")]
    internal static partial int GtkWidgetGetSensitive(nint widget);

    [LibraryImport(Gtk, EntryPoint = "gtk_widget_set_sensitive")]
    internal static partial void GtkWidgetSetSensitive(nint widget, int sensitive);

    [LibraryImport(Gtk, EntryPoint = "gtk_widget_grab_focus")]
    internal static partial void GtkWidgetGrabFocus(nint widget);

    [LibraryImport(Gtk, EntryPoint = "gtk_widget_get_scale_factor")]
    internal static partial int GtkWidgetGetScaleFactor(nint widget);

    [LibraryImport(Gtk, EntryPoint = "gtk_widget_get_allocated_width")]
    internal static partial int GtkWidgetGetAllocatedWidth(nint widget);

    [LibraryImport(Gtk, EntryPoint = "gtk_widget_get_allocated_height")]
    internal static partial int GtkWidgetGetAllocatedHeight(nint widget);

    [LibraryImport(Gtk, EntryPoint = "gtk_widget_get_window")]
    internal static partial nint GtkWidgetGetWindow(nint widget);

    [LibraryImport(Gdk, EntryPoint = "gdk_window_get_state")]
    internal static partial int GdkWindowGetState(nint window);

    [LibraryImport(Gtk, EntryPoint = "gtk_window_present")]
    internal static partial void GtkWindowPresent(nint window);

    [LibraryImport(Gtk, EntryPoint = "gtk_window_maximize")]
    internal static partial void GtkWindowMaximize(nint window);

    [LibraryImport(Gtk, EntryPoint = "gtk_window_unmaximize")]
    internal static partial void GtkWindowUnmaximize(nint window);

    [LibraryImport(Gtk, EntryPoint = "gtk_window_iconify")]
    internal static partial void GtkWindowIconify(nint window);

    [LibraryImport(Gtk, EntryPoint = "gtk_window_deiconify")]
    internal static partial void GtkWindowDeiconify(nint window);

    [LibraryImport(Gtk, EntryPoint = "gtk_widget_destroy")]
    internal static partial void GtkWidgetDestroy(nint widget);

    [LibraryImport(Gtk, EntryPoint = "gtk_main")]
    internal static partial void GtkMain();

    [LibraryImport(Gtk, EntryPoint = "gtk_main_quit")]
    internal static partial void GtkMainQuit();

    [LibraryImport(GObject, EntryPoint = "g_signal_connect_data", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial ulong GSignalConnectData(
        nint instance,
        string detailedSignal,
        delegate* unmanaged[Cdecl]<void> handler,
        nint data,
        nint destroyData,
        int connectFlags);

    [LibraryImport(GObject, EntryPoint = "g_object_unref")]
    internal static partial void GObjectUnref(nint instance);

    [LibraryImport(GObject, EntryPoint = "g_object_ref_sink")]
    internal static partial nint GObjectRefSink(nint instance);

    [LibraryImport(GLib, EntryPoint = "g_main_context_invoke_full")]
    internal static partial void GMainContextInvokeFull(
        nint context,
        int priority,
        delegate* unmanaged[Cdecl]<nint, int> function,
        nint data,
        delegate* unmanaged[Cdecl]<nint, void> notify);

    [LibraryImport(Gio, EntryPoint = "g_memory_input_stream_new_from_data")]
    internal static partial nint GMemoryInputStreamNewFromData(
        nint data,
        nint length,
        delegate* unmanaged[Cdecl]<nint, void> destroy);

    [LibraryImport(GLib, EntryPoint = "g_free")]
    internal static partial void GFree(nint memory);

    [LibraryImport(GLib, EntryPoint = "g_error_free")]
    internal static partial void GErrorFree(nint error);

    [LibraryImport(WebKit, EntryPoint = "webkit_web_context_new")]
    internal static partial nint WebKitWebContextNew();

    [LibraryImport(WebKit, EntryPoint = "webkit_web_context_register_uri_scheme", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void WebKitWebContextRegisterUriScheme(
        nint context,
        string scheme,
        delegate* unmanaged[Cdecl]<nint, nint, void> callback,
        nint userData,
        nint destroyNotify);

    [LibraryImport(WebKit, EntryPoint = "webkit_web_view_new_with_context")]
    internal static partial nint WebKitWebViewNewWithContext(nint context);

    [LibraryImport(WebKit, EntryPoint = "webkit_web_view_get_user_content_manager")]
    internal static partial nint WebKitWebViewGetUserContentManager(nint webView);

    [LibraryImport(WebKit, EntryPoint = "webkit_user_content_manager_register_script_message_handler", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int WebKitUserContentManagerRegisterScriptMessageHandler(nint manager, string name);

    [LibraryImport(WebKit, EntryPoint = "webkit_user_content_manager_unregister_script_message_handler", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void WebKitUserContentManagerUnregisterScriptMessageHandler(nint manager, string name);

    [LibraryImport(WebKit, EntryPoint = "webkit_user_script_new", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint WebKitUserScriptNew(
        string source,
        int injectedFrames,
        int injectionTime,
        nint allowList,
        nint blockList);

    [LibraryImport(WebKit, EntryPoint = "webkit_user_script_unref")]
    internal static partial void WebKitUserScriptUnref(nint script);

    [LibraryImport(WebKit, EntryPoint = "webkit_user_content_manager_add_script")]
    internal static partial void WebKitUserContentManagerAddScript(nint manager, nint script);

    [LibraryImport(WebKit, EntryPoint = "webkit_web_view_load_uri", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void WebKitWebViewLoadUri(nint webView, string uri);

    [LibraryImport(WebKit, EntryPoint = "webkit_web_view_get_uri")]
    internal static partial nint WebKitWebViewGetUri(nint webView);

    [LibraryImport(WebKit, EntryPoint = "webkit_web_view_set_zoom_level")]
    internal static partial void WebKitWebViewSetZoomLevel(nint webView, double zoomLevel);

    [LibraryImport(WebKit, EntryPoint = "webkit_web_view_evaluate_javascript", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void WebKitWebViewEvaluateJavaScript(
        nint webView,
        string script,
        nint length,
        nint worldName,
        nint sourceUri,
        nint cancellable,
        nint callback,
        nint userData);

    [LibraryImport(WebKit, EntryPoint = "webkit_javascript_result_get_js_value")]
    internal static partial nint WebKitJavaScriptResultGetJsValue(nint result);

    [LibraryImport(JavaScriptCore, EntryPoint = "jsc_value_to_string")]
    internal static partial nint JscValueToString(nint value);

    [LibraryImport(WebKit, EntryPoint = "webkit_uri_scheme_request_get_uri")]
    internal static partial nint WebKitUriSchemeRequestGetUri(nint request);

    [LibraryImport(WebKit, EntryPoint = "webkit_uri_scheme_request_get_http_method")]
    internal static partial nint WebKitUriSchemeRequestGetHttpMethod(nint request);

    [LibraryImport(WebKit, EntryPoint = "webkit_uri_scheme_request_get_http_headers")]
    internal static partial nint WebKitUriSchemeRequestGetHttpHeaders(nint request);

    [LibraryImport(WebKit, EntryPoint = "webkit_uri_scheme_response_new")]
    internal static partial nint WebKitUriSchemeResponseNew(nint inputStream, long streamLength);

    [LibraryImport(WebKit, EntryPoint = "webkit_uri_scheme_response_set_status", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void WebKitUriSchemeResponseSetStatus(nint response, uint statusCode, string? reasonPhrase);

    [LibraryImport(WebKit, EntryPoint = "webkit_uri_scheme_response_set_content_type", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void WebKitUriSchemeResponseSetContentType(nint response, string contentType);

    [LibraryImport(WebKit, EntryPoint = "webkit_uri_scheme_response_set_http_headers")]
    internal static partial void WebKitUriSchemeResponseSetHttpHeaders(nint response, nint headers);

    [LibraryImport(WebKit, EntryPoint = "webkit_uri_scheme_request_finish_with_response")]
    internal static partial void WebKitUriSchemeRequestFinishWithResponse(nint request, nint response);

    [LibraryImport(AppIndicator, EntryPoint = "app_indicator_new", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint AppIndicatorNew(string id, string iconName, int category);

    [LibraryImport(AppIndicator, EntryPoint = "app_indicator_set_status")]
    internal static partial void AppIndicatorSetStatus(nint indicator, int status);

    [LibraryImport(AppIndicator, EntryPoint = "app_indicator_set_menu")]
    internal static partial void AppIndicatorSetMenu(nint indicator, nint menu);

    [LibraryImport(AppIndicator, EntryPoint = "app_indicator_set_title", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void AppIndicatorSetTitle(nint indicator, string title);

    [LibraryImport(AppIndicator, EntryPoint = "app_indicator_set_icon_full", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void AppIndicatorSetIconFull(nint indicator, string iconName, string iconDescription);

    [LibraryImport(GdkPixbuf, EntryPoint = "gdk_pixbuf_new")]
    internal static partial nint GdkPixbufNew(int colorspace, int hasAlpha, int bitsPerSample, int width, int height);

    [LibraryImport(GdkPixbuf, EntryPoint = "gdk_pixbuf_get_pixels")]
    internal static partial nint GdkPixbufGetPixels(nint pixbuf);

    [LibraryImport(GdkPixbuf, EntryPoint = "gdk_pixbuf_get_rowstride")]
    internal static partial int GdkPixbufGetRowstride(nint pixbuf);

    [LibraryImport(GdkPixbuf, EntryPoint = "gdk_pixbuf_savev", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int GdkPixbufSaveV(
        nint pixbuf,
        string filename,
        string type,
        nint optionKeys,
        nint optionValues,
        out nint error);

    [LibraryImport(Soup, EntryPoint = "soup_message_headers_new")]
    internal static partial nint SoupMessageHeadersNew(int headersType);

    [LibraryImport(Soup, EntryPoint = "soup_message_headers_append", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void SoupMessageHeadersAppend(nint headers, string name, string value);

    [LibraryImport(Soup, EntryPoint = "soup_message_headers_foreach")]
    internal static partial void SoupMessageHeadersForeach(
        nint headers,
        delegate* unmanaged[Cdecl]<nint, nint, nint, void> callback,
        nint userData);

    [LibraryImport(Soup, EntryPoint = "soup_message_headers_unref")]
    internal static partial void SoupMessageHeadersUnref(nint headers);

    internal static string? Utf8(nint value) => value == 0 ? null : Marshal.PtrToStringUTF8(value);
}
