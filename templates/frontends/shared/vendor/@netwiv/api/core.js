export { SERIALIZE_TO_IPC_FN, transformCallback, Channel, Resource, invoke, convertFileSrc, PluginListener, addPluginListener, checkPermissions, requestPermissions } from '../../@tauri-apps/api/core.js';
/** Whether this page is running inside a NetWiv webview. */
export function isNetWiv() {
    return typeof window !== 'undefined' &&
        window.isNetWiv === true;
}
