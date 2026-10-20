export { SERIALIZE_TO_IPC_FN, transformCallback, Channel, Resource, invoke, convertFileSrc, PluginListener, addPluginListener, checkPermissions, requestPermissions } from './upstream/core.js';
/**
 * Invoke a command using JSON text without converting its numbers to JavaScript
 * numbers. Arguments must encode an object; the result is JSON text.
 * Used by the generated Blazor client. Binary responses are not supported.
 * Parsing the returned text with JSON.parse restores JavaScript's number limits.
 */
export async function invokeJson(cmd, args = '{}', options) {
    const runtime = globalThis.window?.__TAURI_INTERNALS__;
    if (!runtime?.invokeJson)
        throw new Error('This NetWiv runtime does not support invokeJson');
    return runtime.invokeJson(cmd, args, options);
}
