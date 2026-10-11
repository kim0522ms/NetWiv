using System.Runtime.InteropServices;

namespace NetWiv.Platform.Windows.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct Point
{
    internal int X;
    internal int Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct Message
{
    internal nint HWnd;
    internal uint Value;
    internal nuint WParam;
    internal nint LParam;
    internal uint Time;
    internal Point Point;
    internal uint Private;
}

[StructLayout(LayoutKind.Sequential)]
internal struct Rect
{
    internal int Left;
    internal int Top;
    internal int Right;
    internal int Bottom;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal unsafe struct WindowClass
{
    internal uint Size;
    internal uint Style;
    internal delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nint> WindowProcedure;
    internal int ClassExtra;
    internal int WindowExtra;
    internal nint Instance;
    internal nint Icon;
    internal nint Cursor;
    internal nint BackgroundBrush;
    internal char* MenuName;
    internal char* ClassName;
    internal nint SmallIcon;
}

[StructLayout(LayoutKind.Sequential)]
internal struct EventRegistrationToken
{
    internal long Value;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal unsafe struct NotifyIconData
{
    internal uint Size;
    internal nint Window;
    internal uint Id;
    internal uint Flags;
    internal uint CallbackMessage;
    internal nint Icon;
    internal fixed char Tip[128];
    internal uint State;
    internal uint StateMask;
    internal fixed char Info[256];
    internal uint VersionOrTimeout;
    internal fixed char InfoTitle[64];
    internal uint InfoFlags;
    internal Guid ItemGuid;
    internal nint BalloonIcon;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NotifyIconIdentifier
{
    internal uint Size;
    internal nint Window;
    internal uint Id;
    internal Guid ItemGuid;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct MenuItemInfo
{
    internal uint Size;
    internal uint Mask;
    internal uint Type;
    internal uint State;
    internal uint Id;
    internal nint Submenu;
    internal nint CheckedBitmap;
    internal nint UncheckedBitmap;
    internal nuint ItemData;
    internal nint TypeData;
    internal uint TextLength;
    internal nint ItemBitmap;
}
