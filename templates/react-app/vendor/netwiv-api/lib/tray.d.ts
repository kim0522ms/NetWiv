import { TrayIcon as UpstreamTrayIcon } from './upstream/tray.js';
import type { TrayIconOptions as UpstreamTrayIconOptions } from './upstream/tray.js';
import type { Menu, Submenu } from './menu.js';
export type { MouseButtonState, MouseButton, TrayIconEventType, TrayIconEventBase, TrayIconClickEvent, TrayIconEvent } from './upstream/tray.js';
export interface TrayIconOptions extends Omit<UpstreamTrayIconOptions, 'menu'> {
    menu?: Menu | Submenu;
}
export interface TrayIcon extends UpstreamTrayIcon {
    setMenu(menu: Menu | Submenu | null): Promise<void>;
}
export declare const TrayIcon: {
    readonly prototype: TrayIcon;
    "new"(options?: TrayIconOptions): Promise<TrayIcon>;
    getById(id: string): Promise<TrayIcon | null>;
    removeById(id: string): Promise<void>;
};
