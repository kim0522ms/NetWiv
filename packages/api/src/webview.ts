import { Webview as UpstreamWebview } from '@tauri-apps/api/webview';
import type { WebviewOptions as UpstreamWebviewOptions } from '@tauri-apps/api/webview';
import type { Window } from './window.js';
import type { WebviewWindow } from './webviewWindow.js';
import { desktopConstructor, desktopHandle } from './desktop.js';
export type { DragDropEvent, Color, WebviewLabel } from '@tauri-apps/api/webview';

export interface Webview extends Omit<UpstreamWebview, 'window' | 'reparent'> {
  window: Window;
  reparent(window: Window | WebviewWindow | string): Promise<void>;
}
export type WebviewOptions = Pick<UpstreamWebviewOptions,
  'url' | 'x' | 'y' | 'width' | 'height' | 'transparent' | 'focus' |
  'dragDropEnabled' | 'acceptFirstMouse' | 'userAgent' | 'incognito' | 'proxyUrl' |
  'zoomHotkeysEnabled' | 'useHttpsScheme' | 'devtools' | 'backgroundColor' |
  'backgroundThrottling' | 'javascriptDisabled' | 'allowLinkPreview' |
  'dataDirectory' | 'dataStoreIdentifier' | 'scrollBarStyle' | 'generalAutofillEnabled'
>;
export function getCurrentWebview(): Webview {
  return desktopHandle(UpstreamWebview.getCurrent()) as Webview;
}
export async function getAllWebviews(): Promise<Webview[]> {
  return (await UpstreamWebview.getAll()).map(desktopHandle) as Webview[];
}
async function getByLabel(label: string): Promise<Webview | null> {
  const handle = await UpstreamWebview.getByLabel(label);
  return handle && desktopHandle(handle) as Webview;
}
export const Webview = desktopConstructor(UpstreamWebview, 2, {
  getCurrent: getCurrentWebview, getAll: getAllWebviews, getByLabel
}) as {
  new(window: Window, label: string, options: WebviewOptions): Webview;
  readonly prototype: Webview;
  getCurrent: typeof getCurrentWebview;
  getAll: typeof getAllWebviews;
  getByLabel: typeof getByLabel;
};
