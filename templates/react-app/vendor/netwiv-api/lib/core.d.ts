export { SERIALIZE_TO_IPC_FN, transformCallback, Channel, Resource, invoke, convertFileSrc, PluginListener, addPluginListener, checkPermissions, requestPermissions } from './upstream/core.js';
export type { InvokeArgs, InvokeOptions } from './upstream/core.js';
export type PermissionState = 'granted' | 'denied' | 'prompt';
