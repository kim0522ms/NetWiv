import { sendNotification as upstreamSendNotification } from '../upstream/plugins/notification.js';
export { isPermissionGranted, requestPermission } from '../upstream/plugins/notification.js';
export function sendNotification(options) {
    if (typeof options !== 'string') {
        for (const key of Object.keys(options)) {
            if (!['title', 'body', 'icon', 'sound'].includes(key)) {
                throw new TypeError(`@netwiv/api does not support notification option '${key}'.`);
            }
        }
    }
    upstreamSendNotification(options);
}
