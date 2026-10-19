import { Menu as UpstreamMenu, Submenu as UpstreamSubmenu, itemFromKind as upstreamItemFromKind } from './upstream/menu.js';
import type { MenuOptions as UpstreamMenuOptions, SubmenuOptions as UpstreamSubmenuOptions, MenuItem, CheckMenuItem, IconMenuItem, PredefinedMenuItem, MenuItemOptions, CheckMenuItemOptions, IconMenuItemOptions, PredefinedMenuItemOptions } from './upstream/menu.js';
import type { LogicalPosition, PhysicalPosition, Position } from './dpi.js';
import type { Window } from './window.js';
export { MenuItem, CheckMenuItem, IconMenuItem, PredefinedMenuItem, NativeIcon } from './upstream/menu.js';
export type { MenuItemOptions, CheckMenuItemOptions, IconMenuItemOptions, PredefinedMenuItemOptions, AboutMetadata } from './upstream/menu.js';
type Item = Submenu | MenuItem | PredefinedMenuItem | CheckMenuItem | IconMenuItem;
type Entry = Item | MenuItemOptions | SubmenuOptions | CheckMenuItemOptions | IconMenuItemOptions | PredefinedMenuItemOptions;
export interface MenuOptions extends Omit<UpstreamMenuOptions, 'items'> {
    items?: Entry[];
}
export interface SubmenuOptions extends Omit<UpstreamSubmenuOptions, 'items'> {
    items?: Entry[];
}
export interface Menu extends UpstreamMenu {
    append<T extends Entry>(items: T | T[]): Promise<void>;
    prepend<T extends Entry>(items: T | T[]): Promise<void>;
    insert<T extends Entry>(items: T | T[], position: number): Promise<void>;
    remove(item: Item): Promise<void>;
    popup(at?: PhysicalPosition | LogicalPosition | Position, window?: Window): Promise<void>;
    setAsWindowMenu(window?: Window): Promise<Menu | null>;
    setAsAppMenu(): Promise<Menu | null>;
    items(): Promise<Item[]>;
    get(id: string): Promise<Item | null>;
    removeAt(position: number): Promise<Item | null>;
}
export interface Submenu extends UpstreamSubmenu {
    append<T extends Entry>(items: T | T[]): Promise<void>;
    prepend<T extends Entry>(items: T | T[]): Promise<void>;
    insert<T extends Entry>(items: T | T[], position: number): Promise<void>;
    remove(item: Item): Promise<void>;
    popup(at?: PhysicalPosition | LogicalPosition, window?: Window): Promise<void>;
    items(): Promise<Item[]>;
    get(id: string): Promise<Item | null>;
    removeAt(position: number): Promise<Item | null>;
}
export declare const Menu: {
    readonly prototype: Menu;
    "new"(options?: MenuOptions): Promise<Menu>;
    default(): Promise<Menu>;
};
export declare const Submenu: {
    readonly prototype: Submenu;
    "new"(options: SubmenuOptions): Promise<Submenu>;
};
export declare const itemFromKind: (value: Parameters<typeof upstreamItemFromKind>[0]) => Item;
