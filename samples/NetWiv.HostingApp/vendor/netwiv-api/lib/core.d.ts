export { SERIALIZE_TO_IPC_FN, transformCallback, Channel, Resource, invoke, convertFileSrc, PluginListener, addPluginListener, checkPermissions, requestPermissions } from './upstream/core.js';
export type { InvokeArgs, InvokeOptions } from './upstream/core.js';
export type PermissionState = 'granted' | 'denied' | 'prompt';
import type { InvokeOptions } from './upstream/core.js';
/**
 * Invoke a command using JSON text without converting its numbers to JavaScript
 * numbers. Arguments must encode an object; the result is JSON text.
 * Used by the generated Blazor client. Binary responses are not supported.
 * Parsing the returned text with JSON.parse restores JavaScript's number limits.
 */
export declare function invokeJson(cmd: string, args?: string, options?: InvokeOptions): Promise<string>;
/**
 * Resolve a real file's native path from a top-level Windows WebView2 page.
 * The `core:webview:allow-get-file-path` capability is required and is excluded from defaults.
 * Rejects with `FilePathUnavailable` when the file has no local backing file, or
 * `FilePathNotSupported` on unsupported hosts, frames or runtimes. A path does not
 * grant filesystem access.
 */
export declare function getFilePath(file: File): Promise<string>;
