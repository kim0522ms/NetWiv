import { Window as UpstreamWindow } from '@tauri-apps/api/window';
import { desktopConstructor, desktopHandle } from './desktop.js';
export { CloseRequestedEvent, LogicalSize, PhysicalSize, LogicalPosition, PhysicalPosition, UserAttentionType, Effect, EffectState, ProgressBarStatus, currentMonitor, monitorFromPoint, primaryMonitor, availableMonitors, cursorPosition } from '@tauri-apps/api/window';
export function getCurrentWindow() {
    return desktopHandle(UpstreamWindow.getCurrent());
}
export async function getAllWindows() {
    return (await UpstreamWindow.getAll()).map(desktopHandle);
}
async function getByLabel(label) {
    const handle = await UpstreamWindow.getByLabel(label);
    return handle && desktopHandle(handle);
}
async function getFocusedWindow() {
    const handle = await UpstreamWindow.getFocusedWindow();
    return handle && desktopHandle(handle);
}
export const Window = desktopConstructor(UpstreamWindow, 1, {
    getCurrent: getCurrentWindow, getAll: getAllWindows, getByLabel, getFocusedWindow
});
