import { Webview as UpstreamWebview } from '@tauri-apps/api/webview';
import { desktopConstructor, desktopHandle } from './desktop.js';
export function getCurrentWebview() {
    return desktopHandle(UpstreamWebview.getCurrent());
}
export async function getAllWebviews() {
    return (await UpstreamWebview.getAll()).map(desktopHandle);
}
async function getByLabel(label) {
    const handle = await UpstreamWebview.getByLabel(label);
    return handle && desktopHandle(handle);
}
export const Webview = desktopConstructor(UpstreamWebview, 2, {
    getCurrent: getCurrentWebview, getAll: getAllWebviews, getByLabel
});
