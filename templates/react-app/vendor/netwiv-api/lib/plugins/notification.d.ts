export { isPermissionGranted, requestPermission } from '../upstream/plugins/notification.js';
export type { PermissionState } from '../core.js';
/** Desktop notification fields supported by NetWiv. */
export interface Options {
    title: string;
    body?: string;
    icon?: string;
    sound?: string;
}
export declare function sendNotification(options: Options | string): void;
