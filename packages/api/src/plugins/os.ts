import { platform as upstreamPlatform, type as upstreamType, arch as upstreamArch } from '@tauri-apps/plugin-os';
export { eol, family, version, locale, exeExtension, hostname } from '@tauri-apps/plugin-os';
export type { Family } from '@tauri-apps/plugin-os';
export type Platform = 'linux' | 'macos' | 'windows';
export type OsType = Platform;
export type Arch = 'x86_64' | 'aarch64';
export function platform(): Platform { return upstreamPlatform() as Platform; }
export function type(): OsType { return upstreamType() as OsType; }
export function arch(): Arch { return upstreamArch() as Arch; }
