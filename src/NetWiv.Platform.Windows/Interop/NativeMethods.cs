using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NetWiv.Platform.Windows.Interop;

internal static partial class NativeMethods
{
    internal const uint CsOwnDc = 0x0020;
    internal const uint WmDestroy = 0x0002;
    internal const uint WmSize = 0x0005;
    internal const uint WmClose = 0x0010;
    internal const uint WmDpiChanged = 0x02E0;
    internal const uint WmAppDispatch = 0x8001;
    internal const uint WmAppTray = 0x8002;
    internal const uint WmCommand = 0x0111;
    internal const int SwShow = 5;
    internal const int SwHide = 0;
    internal const int SwMinimize = 6;
    internal const int SwMaximize = 3;
    internal const uint WsOverlappedWindow = 0x00CF0000;
    internal const uint WsVisible = 0x10000000;
    internal const uint WsChild = 0x40000000;
    internal const uint CwUseDefault = 0x80000000;
    internal const uint SwpNoZOrder = 0x0004;
    internal const uint SwpNoActivate = 0x0010;
    internal const int GwlStyle = -16;
    internal const uint WsThickFrame = 0x00040000;
    internal const uint WsCaption = 0x00C00000;
    internal const uint SwpFrameChanged = 0x0020;
    internal static readonly nint HwndTopmost = new(-1);
    internal static readonly nint HwndNotTopmost = new(-2);
    internal const uint DpiAwarenessContextPerMonitorAwareV2 = unchecked((uint)-4);
    internal const uint CoinitApartmentThreaded = 0x2;
    internal const int ErrorClassAlreadyExists = 1410;

    [LibraryImport("ole32.dll")]
    internal static partial int CoInitializeEx(nint reserved, uint coInit);

    [LibraryImport("ole32.dll")]
    internal static partial void CoUninitialize();

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint GetModuleHandle(string? moduleName);

    [LibraryImport("kernel32.dll")]
    internal static partial uint GetCurrentThreadId();

    [LibraryImport("kernel32.dll")]
    internal static partial uint GetLastError();

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW")]
    internal static unsafe partial ushort RegisterClass(ref WindowClass windowClass);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateWindow(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyWindow(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(nint window, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowVisible(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsZoomed(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsIconic(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(nint window);

    [LibraryImport("user32.dll")]
    internal static partial nint SetFocus(nint window);

    [LibraryImport("user32.dll")]
    internal static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UpdateWindow(nint window);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowText(nint window, string text);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetClientRect(nint window, out Rect rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowRect(nint window, out Rect rect);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    internal static partial int GetWindowTextLength(nint window);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
    internal static unsafe partial int GetWindowText(nint window, char* text, int maxCount);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static partial nint GetWindowLongPtr(nint window, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    internal static partial nint SetWindowLongPtr(nint window, int index, nint value);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ClientToScreen(nint window, ref Point point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ScreenToClient(nint window, ref Point point);

    [LibraryImport("user32.dll")]
    internal static partial uint GetDpiForWindow(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    internal static partial int GetMessage(out Message message, nint window, uint min, uint max);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TranslateMessage(in Message message);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    internal static partial nint DispatchMessage(in Message message);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    internal static partial nint DefWindowProc(nint window, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    internal static partial nint SendMessage(nint window, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostMessage(nint window, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostThreadMessage(uint threadId, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    internal static partial void PostQuitMessage(int exitCode);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetProcessDpiAwarenessContext(nint value);

    [LibraryImport("user32.dll")]
    internal static partial nint CreateMenu();

    [LibraryImport("user32.dll")]
    internal static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyMenu(nint menu);

    [LibraryImport("user32.dll", EntryPoint = "AppendMenuW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AppendMenu(nint menu, uint flags, nuint item, string? text);

    [LibraryImport("user32.dll")]
    internal static partial int GetMenuItemCount(nint menu);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RemoveMenu(nint menu, uint position, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "SetMenuItemInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetMenuItemInfo(nint menu, uint item, [MarshalAs(UnmanagedType.Bool)] bool byPosition, in MenuItemInfo info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetMenu(nint window, nint menu);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DrawMenuBar(nint window);

    [LibraryImport("user32.dll", EntryPoint = "TrackPopupMenuEx")]
    internal static partial uint TrackPopupMenu(nint menu, uint flags, int x, int y, nint window, nint parameters);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetCursorPos(out Point point);

    [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconGetRect")]
    internal static partial int ShellNotifyIconGetRect(in NotifyIconIdentifier identifier, out Rect rect);

    [LibraryImport("user32.dll")]
    internal static unsafe partial nint CreateIcon(
        nint instance,
        int width,
        int height,
        byte planes,
        byte bitsPerPixel,
        byte* andBits,
        byte* xorBits);

    [LibraryImport("gdi32.dll")]
    internal static unsafe partial nint CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, byte* bits);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteObject(nint value);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyIcon(nint icon);

    internal static Exception Win32Error(string operation) =>
        new InvalidOperationException($"{operation} failed with Win32 error {Marshal.GetLastPInvokeError()}.");

    internal static void ThrowIfFailed(int hresult, [CallerArgumentExpression(nameof(hresult))] string? operation = null)
    {
        if (hresult < 0)
        {
            Marshal.ThrowExceptionForHR(hresult, new nint(-1));
        }
    }
}
