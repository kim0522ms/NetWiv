import { TrayIcon as UpstreamTrayIcon } from '@tauri-apps/api/tray';
import type { TrayIconOptions as UpstreamTrayIconOptions } from '@tauri-apps/api/tray';
import type { Menu, Submenu } from './menu.js';
export type {
  MouseButtonState, MouseButton, TrayIconEventType, TrayIconEventBase,
  TrayIconClickEvent, TrayIconEvent
} from '@tauri-apps/api/tray';

export interface TrayIconOptions extends Omit<UpstreamTrayIconOptions, 'menu'> {
  menu?: Menu | Submenu;
}
export interface TrayIcon extends UpstreamTrayIcon {
  setMenu(menu: Menu | Submenu | null): Promise<void>;
}
export const TrayIcon = UpstreamTrayIcon as unknown as {
  readonly prototype: TrayIcon;
  'new'(options?: TrayIconOptions): Promise<TrayIcon>;
  getById(id: string): Promise<TrayIcon | null>;
  removeById(id: string): Promise<void>;
};
