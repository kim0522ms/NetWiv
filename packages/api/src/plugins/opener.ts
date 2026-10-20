import { openUrl as upstreamOpenUrl } from '@tauri-apps/plugin-opener';
export { openPath, revealItemInDir } from '@tauri-apps/plugin-opener';

/** Open a URL using the default or a named desktop application. */
export async function openUrl(url: string | URL, openWith?: string): Promise<void> {
  if (openWith === 'inAppBrowser') {
    throw new TypeError('@netwiv/api does not support the mobile inAppBrowser option.');
  }
  return upstreamOpenUrl(url, openWith);
}
