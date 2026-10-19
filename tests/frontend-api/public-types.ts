import * as api from '@netwiv/api';
import * as app from '@netwiv/api/app';
import * as core from '@netwiv/api/core';
import * as dpi from '@netwiv/api/dpi';
import * as event from '@netwiv/api/event';
import * as image from '@netwiv/api/image';
import * as menu from '@netwiv/api/menu';
import * as path from '@netwiv/api/path';
import * as resources from '@netwiv/api/resources';
import * as tray from '@netwiv/api/tray';
import * as window from '@netwiv/api/window';
import * as webview from '@netwiv/api/webview';
import * as webviewWindow from '@netwiv/api/webviewWindow';
import type { Channel as OfficialChannel, Resource as OfficialResource } from '@tauri-apps/api/core';

const detected: boolean = api.isNetWiv() && core.isNetWiv();
const result: Promise<string> = core.invoke<string>('greet', { name: 'fixture' });
const options: core.InvokeOptions = { headers: { 'x-fixture': 'value' } };
const channel = new core.Channel<string>();
channel.onmessage = value => { const message: string = value; void message; };
declare const officialChannel: OfficialChannel<string>;
declare const officialResource: OfficialResource;
const migratedChannel: core.Channel<string> = officialChannel;
const migratedResource: core.Resource = officialResource;
const compatibleChannel: OfficialChannel<string> = channel;
const compatibleResource: OfficialResource = migratedResource;
const permission: core.PermissionState = 'prompt';
const permissionCheck: Promise<{ fixture: core.PermissionState }> = core.checkPermissions('fixture');
const listener: Promise<core.PluginListener> = core.addPluginListener('fixture', 'event', () => {});
const desktopMenu = await menu.Menu.new();
await desktopMenu.popup(undefined, window.getCurrentWindow());
await desktopMenu.setAsWindowMenu(window.getCurrentWindow());
const trayOptions: tray.TrayIconOptions = { menu: desktopMenu };
await trayOptions.menu?.popup(undefined, window.getCurrentWindow());
const submenu = await menu.Submenu.new({ text: 'fixture', items: [] });
await desktopMenu.append(submenu);
await submenu.prepend({ text: 'fixture', items: [] });
void [detected, result, options, migratedChannel, migratedResource, compatibleChannel, compatibleResource,
  permission, permissionCheck, listener, app, dpi, event, image, menu, path, resources, tray, window, webview, webviewWindow];

// @ts-expect-error Android activity APIs are outside the desktop window contract.
window.getCurrentWindow().activityName();
// @ts-expect-error iOS scene APIs are outside the desktop window contract.
webviewWindow.getCurrentWebviewWindow().sceneIdentifier();
declare const popupWindow: NonNullable<Parameters<menu.Menu['popup']>[1]>;
// @ts-expect-error Menu parameters must preserve the desktop window contract.
popupWindow.activityName();
// @ts-expect-error Mobile window creation options are outside the desktop contract.
const mobileWindow: window.WindowOptions = { activityName: 'fixture' };
// @ts-expect-error Mobile webview creation options are outside the desktop contract.
const mobileWebview: webview.WebviewOptions = { x: 0, y: 0, width: 1, height: 1, disableInputAccessoryView: true };
// @ts-expect-error The Android permission rationale state is outside desktop permissions.
const mobilePermission: core.PermissionState = 'prompt-with-rationale';
// @ts-expect-error The Android back button handler is outside the desktop app API.
app.onBackButtonPress(() => {});
// @ts-expect-error Mobile lifecycle events are outside the desktop event inventory.
event.TauriEvent.WINDOW_SUSPENDED;
// @ts-expect-error The upstream runtime marker is not a NetWiv API.
core.isTauri();
// @ts-expect-error Mock helpers belong to the unchanged upstream package.
api.mocks;
// @ts-expect-error Unlisted package subpaths cannot bypass the public exports.
import * as mocks from '@netwiv/api/mocks';
// @ts-expect-error Package internals are not public import paths.
import * as internal from '@netwiv/api/lib/core.js';
void [mocks, internal, mobileWindow, mobileWebview, mobilePermission];
