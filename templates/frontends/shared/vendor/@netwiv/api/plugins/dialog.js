export { save, message, ask, confirm } from '../upstream/plugins/dialog.js';
import { open as upstreamOpen } from '../upstream/plugins/dialog.js';
export async function open(options) {
    for (const key of ['pickerMode', 'fileAccessMode']) {
        if (options && key in options)
            throw new TypeError(`@netwiv/api does not support dialog option '${key}'.`);
    }
    return upstreamOpen(options);
}
