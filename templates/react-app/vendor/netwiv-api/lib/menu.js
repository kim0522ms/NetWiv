import { Menu as UpstreamMenu, Submenu as UpstreamSubmenu, itemFromKind as upstreamItemFromKind } from '@tauri-apps/api/menu';
export { MenuItem, CheckMenuItem, IconMenuItem, PredefinedMenuItem, NativeIcon } from '@tauri-apps/api/menu';
export const Menu = UpstreamMenu;
export const Submenu = UpstreamSubmenu;
export const itemFromKind = upstreamItemFromKind;
