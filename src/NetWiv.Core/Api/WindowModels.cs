namespace NetWiv;

public enum Theme
{
    Light,
    Dark,
}

public enum TitleBarStyle { Visible, Transparent, Overlay }
public enum BackgroundThrottlingPolicy { Disabled, Throttle, Suspend }
public enum ScrollBarStyle { Default, FluentOverlay }
public enum EffectState { FollowsWindowActiveState, Active, Inactive }
public enum Effect
{
    AppearanceBased, Light, Dark, MediumLight, UltraDark, Titlebar, Selection,
    Menu, Popover, Sidebar, HeaderView, Sheet, WindowBackground, HudWindow,
    FullScreenUI, Tooltip, ContentBackground, UnderWindowBackground,
    UnderPageBackground, Mica, Blur, Acrylic, Tabbed, TabbedDark, TabbedLight,
}

public sealed record Effects(IReadOnlyList<Effect> Values, EffectState? State = null, double? Radius = null, RgbaColor? Color = null);

public enum UserAttentionType
{
    Critical = 1,
    Informational = 2,
}

public enum ResizeDirection
{
    East,
    North,
    NorthEast,
    NorthWest,
    South,
    SouthEast,
    SouthWest,
    West,
}

public enum CursorIcon
{
    Default,
    Crosshair,
    Hand,
    Arrow,
    Move,
    Text,
    Wait,
    Help,
    Progress,
    NotAllowed,
    ContextMenu,
    Cell,
    VerticalText,
    Alias,
    Copy,
    NoDrop,
    Grab,
    Grabbing,
    AllScroll,
    ZoomIn,
    ZoomOut,
    EResize,
    NResize,
    NeResize,
    NwResize,
    SResize,
    SeResize,
    SwResize,
    WResize,
    EwResize,
    NsResize,
    NeswResize,
    NwseResize,
    ColResize,
    RowResize,
}

public enum ProgressBarStatus
{
    None,
    Normal,
    Indeterminate,
    Paused,
    Error,
}

public sealed record WindowSizeConstraints(
    double? MinWidth = null,
    double? MinHeight = null,
    double? MaxWidth = null,
    double? MaxHeight = null);

public sealed record ProgressBarState(
    ProgressBarStatus? Status = null,
    ulong? Progress = null);

public readonly record struct RgbaColor(byte Red, byte Green, byte Blue, byte Alpha = byte.MaxValue);

public sealed record Monitor(
    string? Name,
    PhysicalSize Size,
    PhysicalPosition Position,
    PhysicalPosition WorkAreaPosition,
    PhysicalSize WorkAreaSize,
    double ScaleFactor);
