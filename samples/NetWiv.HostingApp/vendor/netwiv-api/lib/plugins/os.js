import { platform as upstreamPlatform, type as upstreamType, arch as upstreamArch } from '../upstream/plugins/os.js';
export { eol, family, version, locale, exeExtension, hostname } from '../upstream/plugins/os.js';
export function platform() { return upstreamPlatform(); }
export function type() { return upstreamType(); }
export function arch() { return upstreamArch(); }
