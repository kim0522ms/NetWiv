import { WebviewWindow as UpstreamWebviewWindow } from './upstream/webviewWindow.js';
import { desktopConstructor, desktopHandle } from './desktop.js';
export function getCurrentWebviewWindow() {
    return desktopHandle(UpstreamWebviewWindow.getCurrent());
}
export async function getAllWebviewWindows() {
    return (await UpstreamWebviewWindow.getAll()).map(desktopHandle);
}
async function getByLabel(label) {
    const handle = await UpstreamWebviewWindow.getByLabel(label);
    return handle && desktopHandle(handle);
}
export const WebviewWindow = desktopConstructor(UpstreamWebviewWindow, 1, {
    getCurrent: getCurrentWebviewWindow, getAll: getAllWebviewWindows, getByLabel
});
