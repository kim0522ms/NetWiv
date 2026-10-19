export { eol, family, version, locale, exeExtension, hostname } from '../upstream/plugins/os.js';
export type { Family } from '../upstream/plugins/os.js';
export type Platform = 'linux' | 'macos' | 'windows';
export type OsType = Platform;
export type Arch = 'x86_64' | 'aarch64';
export declare function platform(): Platform;
export declare function type(): OsType;
export declare function arch(): Arch;
