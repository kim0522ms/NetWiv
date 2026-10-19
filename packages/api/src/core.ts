export {
  SERIALIZE_TO_IPC_FN, transformCallback, Channel, Resource, invoke, convertFileSrc,
  PluginListener, addPluginListener, checkPermissions, requestPermissions
} from '@tauri-apps/api/core';
export type { InvokeArgs, InvokeOptions } from '@tauri-apps/api/core';
export type PermissionState = 'granted' | 'denied' | 'prompt';

/** Whether this page is running inside a NetWiv webview. */
export function isNetWiv(): boolean {
  return typeof window !== 'undefined' &&
    (window as typeof window & { isNetWiv?: boolean }).isNetWiv === true;
}
