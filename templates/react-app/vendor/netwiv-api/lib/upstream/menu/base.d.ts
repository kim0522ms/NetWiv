import { Resource } from '@tauri-apps/api/core';
import { CheckMenuItemOptions } from './checkMenuItem.js';
import { IconMenuItemOptions } from './iconMenuItem.js';
import { MenuOptions } from './menu.js';
import { MenuItemOptions } from './menuItem.js';
import { PredefinedMenuItemOptions } from './predefinedMenuItem.js';
import { SubmenuOptions } from './submenu.js';
export type ItemKind = 'MenuItem' | 'Predefined' | 'Check' | 'Icon' | 'Submenu' | 'Menu';
export declare function newMenu(kind: ItemKind, opts?: MenuOptions | MenuItemOptions | SubmenuOptions | PredefinedMenuItemOptions | CheckMenuItemOptions | IconMenuItemOptions): Promise<[number, string]>;
export declare class MenuItemBase extends Resource {
    #private;
    /** The id of this item. */
    get id(): string;
    /** @ignore */
    get kind(): string;
    /** @ignore */
    protected constructor(rid: number, id: string, kind: ItemKind);
}
