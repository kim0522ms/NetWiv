import { Menu as UpstreamMenu, Submenu as UpstreamSubmenu, itemFromKind as upstreamItemFromKind } from './upstream/menu.js';
export { MenuItem, CheckMenuItem, IconMenuItem, PredefinedMenuItem, NativeIcon } from './upstream/menu.js';
export const Menu = UpstreamMenu;
export const Submenu = UpstreamSubmenu;
export const itemFromKind = upstreamItemFromKind;
