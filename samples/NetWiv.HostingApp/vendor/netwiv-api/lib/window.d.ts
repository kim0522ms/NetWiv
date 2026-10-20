import { Window as UpstreamWindow } from './upstream/window.js';
import type { WindowOptions as UpstreamWindowOptions } from './upstream/window.js';
import type { WebviewWindow } from './webviewWindow.js';
export { CloseRequestedEvent, LogicalSize, PhysicalSize, LogicalPosition, PhysicalPosition, UserAttentionType, Effect, EffectState, ProgressBarStatus, currentMonitor, monitorFromPoint, primaryMonitor, availableMonitors, cursorPosition } from './upstream/window.js';
export type { Monitor, CursorIcon, WindowSizeConstraints, ProgressBarState, Effects, Theme, TitleBarStyle, ScaleFactorChanged, Color, BackgroundThrottlingPolicy, DragDropEvent, ScrollBarStyle, WindowLabel } from './upstream/window.js';
export type Window = Omit<UpstreamWindow, 'activityName' | 'sceneIdentifier'>;
export interface WindowOptions extends Pick<UpstreamWindowOptions, 'center' | 'x' | 'y' | 'width' | 'height' | 'minWidth' | 'minHeight' | 'maxWidth' | 'maxHeight' | 'preventOverflow' | 'resizable' | 'title' | 'fullscreen' | 'focus' | 'focusable' | 'transparent' | 'maximized' | 'visible' | 'decorations' | 'alwaysOnTop' | 'alwaysOnBottom' | 'contentProtected' | 'skipTaskbar' | 'shadow' | 'theme' | 'titleBarStyle' | 'trafficLightPosition' | 'hiddenTitle' | 'tabbingIdentifier' | 'maximizable' | 'minimizable' | 'closable' | 'visibleOnAllWorkspaces' | 'windowEffects' | 'backgroundColor' | 'backgroundThrottling' | 'javascriptDisabled' | 'allowLinkPreview' | 'scrollBarStyle'> {
    parent?: Window | WebviewWindow | string;
}
export declare function getCurrentWindow(): Window;
export declare function getAllWindows(): Promise<Window[]>;
declare function getByLabel(label: string): Promise<Window | null>;
declare function getFocusedWindow(): Promise<Window | null>;
export declare const Window: {
    new (label: string, options?: WindowOptions): Window;
    readonly prototype: Window;
    getCurrent: typeof getCurrentWindow;
    getAll: typeof getAllWindows;
    getByLabel: typeof getByLabel;
    getFocusedWindow: typeof getFocusedWindow;
};
