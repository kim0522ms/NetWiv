import { WebviewWindow as UpstreamWebviewWindow } from './upstream/webviewWindow.js';
import type { WindowOptions } from './window.js';
import type { Webview, WebviewOptions } from './webview.js';
export type { DragDropEvent, Color } from './webview.js';
export interface WebviewWindow extends Omit<UpstreamWebviewWindow, 'activityName' | 'sceneIdentifier' | 'window' | 'reparent'> {
    window: Webview['window'];
    reparent: Webview['reparent'];
}
export type WebviewWindowOptions = Omit<WebviewOptions, 'x' | 'y' | 'width' | 'height'> & WindowOptions;
export declare function getCurrentWebviewWindow(): WebviewWindow;
export declare function getAllWebviewWindows(): Promise<WebviewWindow[]>;
declare function getByLabel(label: string): Promise<WebviewWindow | null>;
export declare const WebviewWindow: {
    new (label: string, options?: WebviewWindowOptions): WebviewWindow;
    readonly prototype: WebviewWindow;
    getCurrent: typeof getCurrentWebviewWindow;
    getAll: typeof getAllWebviewWindows;
    getByLabel: typeof getByLabel;
};
