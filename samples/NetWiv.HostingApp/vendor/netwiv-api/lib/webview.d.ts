import { Webview as UpstreamWebview } from './upstream/webview.js';
import type { WebviewOptions as UpstreamWebviewOptions } from './upstream/webview.js';
import type { Window } from './window.js';
import type { WebviewWindow } from './webviewWindow.js';
export type { DragDropEvent, Color, WebviewLabel } from './upstream/webview.js';
export interface Webview extends Omit<UpstreamWebview, 'window' | 'reparent'> {
    window: Window;
    reparent(window: Window | WebviewWindow | string): Promise<void>;
}
export type WebviewOptions = Pick<UpstreamWebviewOptions, 'url' | 'x' | 'y' | 'width' | 'height' | 'transparent' | 'focus' | 'dragDropEnabled' | 'acceptFirstMouse' | 'userAgent' | 'incognito' | 'proxyUrl' | 'zoomHotkeysEnabled' | 'useHttpsScheme' | 'devtools' | 'backgroundColor' | 'backgroundThrottling' | 'javascriptDisabled' | 'allowLinkPreview' | 'dataDirectory' | 'dataStoreIdentifier' | 'scrollBarStyle' | 'generalAutofillEnabled'>;
export declare function getCurrentWebview(): Webview;
export declare function getAllWebviews(): Promise<Webview[]>;
declare function getByLabel(label: string): Promise<Webview | null>;
export declare const Webview: {
    new (window: Window, label: string, options: WebviewOptions): Webview;
    readonly prototype: Webview;
    getCurrent: typeof getCurrentWebview;
    getAll: typeof getAllWebviews;
    getByLabel: typeof getByLabel;
};
