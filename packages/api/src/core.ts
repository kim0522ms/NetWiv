export {
  SERIALIZE_TO_IPC_FN, transformCallback, Channel, Resource, invoke, convertFileSrc,
  PluginListener, addPluginListener, checkPermissions, requestPermissions
} from '@tauri-apps/api/core';
export type { InvokeArgs, InvokeOptions } from '@tauri-apps/api/core';
export type PermissionState = 'granted' | 'denied' | 'prompt';

import type { InvokeOptions } from '@tauri-apps/api/core';

/**
 * Invoke a command using JSON text without converting its numbers to JavaScript
 * numbers. Arguments must encode an object; the result is JSON text.
 * Used by the generated Blazor client. Binary responses are not supported.
 * Parsing the returned text with JSON.parse restores JavaScript's number limits.
 */
export async function invokeJson(cmd: string, args = '{}', options?: InvokeOptions): Promise<string> {
  const runtime = (globalThis as typeof globalThis & {
    window?: { __TAURI_INTERNALS__?: {
      invokeJson?: (cmd: string, args: string, options?: InvokeOptions) => Promise<string>
    } }
  }).window?.__TAURI_INTERNALS__;
  if (!runtime?.invokeJson) throw new Error('This NetWiv runtime does not support invokeJson');
  return runtime.invokeJson(cmd, args, options);
}

/**
 * Resolve a real file's native path from a top-level Windows WebView2 page.
 * The `core:webview:allow-get-file-path` capability is required and is excluded from defaults.
 * Rejects with `FilePathUnavailable` when the file has no local backing file, or
 * `FilePathNotSupported` on unsupported hosts, frames or runtimes. A path does not
 * grant filesystem access.
 */
export async function getFilePath(file: File): Promise<string> {
  const runtime = (globalThis as typeof globalThis & {
    window?: { __TAURI_INTERNALS__?: {
      getFilePath?: (file: File) => Promise<string>
    } }
  }).window?.__TAURI_INTERNALS__;
  if (!runtime?.getFilePath) throw new Error('FilePathNotSupported');
  return runtime.getFilePath(file);
}
