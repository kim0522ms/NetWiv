export { openPath, revealItemInDir } from '../upstream/plugins/opener.js';
/** Open a URL using the default or a named desktop application. */
export declare function openUrl(url: string | URL, openWith?: string): Promise<void>;
