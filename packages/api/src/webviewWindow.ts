import { WebviewWindow as UpstreamWebviewWindow } from '@tauri-apps/api/webviewWindow';
import type { WindowOptions } from './window.js';
import type { Webview, WebviewOptions } from './webview.js';
import { desktopConstructor, desktopHandle } from './desktop.js';
export type { DragDropEvent, Color } from './webview.js';

export interface WebviewWindow extends Omit<UpstreamWebviewWindow,
  'activityName' | 'sceneIdentifier' | 'window' | 'reparent'
> {
  window: Webview['window'];
  reparent: Webview['reparent'];
}
export type WebviewWindowOptions = Omit<WebviewOptions, 'x' | 'y' | 'width' | 'height'> & WindowOptions;
export function getCurrentWebviewWindow(): WebviewWindow {
  return desktopHandle(UpstreamWebviewWindow.getCurrent()) as WebviewWindow;
}
export async function getAllWebviewWindows(): Promise<WebviewWindow[]> {
  return (await UpstreamWebviewWindow.getAll()).map(desktopHandle) as WebviewWindow[];
}
async function getByLabel(label: string): Promise<WebviewWindow | null> {
  const handle = await UpstreamWebviewWindow.getByLabel(label);
  return handle && desktopHandle(handle) as WebviewWindow;
}
export const WebviewWindow = desktopConstructor(UpstreamWebviewWindow, 1, {
  getCurrent: getCurrentWebviewWindow, getAll: getAllWebviewWindows, getByLabel
}) as {
  new(label: string, options?: WebviewWindowOptions): WebviewWindow;
  readonly prototype: WebviewWindow;
  getCurrent: typeof getCurrentWebviewWindow;
  getAll: typeof getAllWebviewWindows;
  getByLabel: typeof getByLabel;
};
