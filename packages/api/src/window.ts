import { Window as UpstreamWindow } from '@tauri-apps/api/window';
import type { WindowOptions as UpstreamWindowOptions } from '@tauri-apps/api/window';
import type { WebviewWindow } from './webviewWindow.js';
import { desktopConstructor, desktopHandle } from './desktop.js';

export {
  CloseRequestedEvent, LogicalSize, PhysicalSize, LogicalPosition, PhysicalPosition,
  UserAttentionType, Effect, EffectState, ProgressBarStatus, currentMonitor,
  monitorFromPoint, primaryMonitor, availableMonitors, cursorPosition
} from '@tauri-apps/api/window';
export type {
  Monitor, CursorIcon, WindowSizeConstraints, ProgressBarState, Effects, Theme,
  TitleBarStyle, ScaleFactorChanged, Color, BackgroundThrottlingPolicy,
  DragDropEvent, ScrollBarStyle, WindowLabel
} from '@tauri-apps/api/window';

export type Window = Omit<UpstreamWindow, 'activityName' | 'sceneIdentifier'>;
export interface WindowOptions extends Pick<UpstreamWindowOptions,
  'center' | 'x' | 'y' | 'width' | 'height' | 'minWidth' | 'minHeight' |
  'maxWidth' | 'maxHeight' | 'preventOverflow' | 'resizable' | 'title' |
  'fullscreen' | 'focus' | 'focusable' | 'transparent' | 'maximized' | 'visible' |
  'decorations' | 'alwaysOnTop' | 'alwaysOnBottom' | 'contentProtected' |
  'skipTaskbar' | 'shadow' | 'theme' | 'titleBarStyle' | 'trafficLightPosition' |
  'hiddenTitle' | 'tabbingIdentifier' | 'maximizable' | 'minimizable' | 'closable' |
  'visibleOnAllWorkspaces' | 'windowEffects' | 'backgroundColor' |
  'backgroundThrottling' | 'javascriptDisabled' | 'allowLinkPreview' | 'scrollBarStyle'
> {
  parent?: Window | WebviewWindow | string;
}

export function getCurrentWindow(): Window {
  return desktopHandle(UpstreamWindow.getCurrent());
}
export async function getAllWindows(): Promise<Window[]> {
  return (await UpstreamWindow.getAll()).map(desktopHandle);
}
async function getByLabel(label: string): Promise<Window | null> {
  const handle = await UpstreamWindow.getByLabel(label);
  return handle && desktopHandle(handle);
}
async function getFocusedWindow(): Promise<Window | null> {
  const handle = await UpstreamWindow.getFocusedWindow();
  return handle && desktopHandle(handle);
}
export const Window = desktopConstructor(UpstreamWindow, 1, {
  getCurrent: getCurrentWindow, getAll: getAllWindows, getByLabel, getFocusedWindow
}) as {
  new(label: string, options?: WindowOptions): Window;
  readonly prototype: Window;
  getCurrent: typeof getCurrentWindow;
  getAll: typeof getAllWindows;
  getByLabel: typeof getByLabel;
  getFocusedWindow: typeof getFocusedWindow;
};
