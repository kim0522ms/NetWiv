import {
  Menu as UpstreamMenu, Submenu as UpstreamSubmenu, itemFromKind as upstreamItemFromKind
} from '@tauri-apps/api/menu';
import type {
  MenuOptions as UpstreamMenuOptions, SubmenuOptions as UpstreamSubmenuOptions,
  MenuItem, CheckMenuItem, IconMenuItem, PredefinedMenuItem, MenuItemOptions,
  CheckMenuItemOptions, IconMenuItemOptions, PredefinedMenuItemOptions
} from '@tauri-apps/api/menu';
import type { LogicalPosition, PhysicalPosition, Position } from './dpi.js';
import type { Window } from './window.js';
export { MenuItem, CheckMenuItem, IconMenuItem, PredefinedMenuItem, NativeIcon } from '@tauri-apps/api/menu';
export type {
  MenuItemOptions, CheckMenuItemOptions,
  IconMenuItemOptions, PredefinedMenuItemOptions, AboutMetadata
} from '@tauri-apps/api/menu';

type Item = Submenu | MenuItem | PredefinedMenuItem | CheckMenuItem | IconMenuItem;
type Entry = Item | MenuItemOptions | SubmenuOptions | CheckMenuItemOptions |
  IconMenuItemOptions | PredefinedMenuItemOptions;
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
export const Menu = UpstreamMenu as unknown as {
  readonly prototype: Menu;
  'new'(options?: MenuOptions): Promise<Menu>;
  default(): Promise<Menu>;
};
export const Submenu = UpstreamSubmenu as unknown as {
  readonly prototype: Submenu;
  'new'(options: SubmenuOptions): Promise<Submenu>;
};
export const itemFromKind = upstreamItemFromKind as (
  value: Parameters<typeof upstreamItemFromKind>[0]
) => Item;
