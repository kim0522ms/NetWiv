import { sendNotification as upstreamSendNotification } from '@tauri-apps/plugin-notification';
export { isPermissionGranted, requestPermission } from '@tauri-apps/plugin-notification';
export type { PermissionState } from '../core.js';

/** Desktop notification fields supported by NetWiv. */
export interface Options {
  title: string;
  body?: string;
  icon?: string;
  sound?: string;
}
export function sendNotification(options: Options | string): void {
  if (typeof options !== 'string') {
    for (const key of Object.keys(options)) {
      if (!['title', 'body', 'icon', 'sound'].includes(key)) {
        throw new TypeError(`@netwiv/api does not support notification option '${key}'.`);
      }
    }
  }
  upstreamSendNotification(options);
}
