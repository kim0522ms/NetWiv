import { openUrl as upstreamOpenUrl } from '../upstream/plugins/opener.js';
export { openPath, revealItemInDir } from '../upstream/plugins/opener.js';
/** Open a URL using the default or a named desktop application. */
export async function openUrl(url, openWith) {
    if (openWith === 'inAppBrowser') {
        throw new TypeError('@netwiv/api does not support the mobile inAppBrowser option.');
    }
    return upstreamOpenUrl(url, openWith);
}
