export { save, message, ask, confirm } from '@tauri-apps/plugin-dialog';
export type { MessageDialogDefaultButtons, MessageDialogButtonsYesNoCancel, MessageDialogButtonsOkCancel, MessageDialogButtonsOk, MessageDialogCustomButtons, MessageDialogButtons, MessageDialogResult, DialogFilter, SaveDialogOptions, MessageDialogOptions, ConfirmDialogOptions } from '@tauri-apps/plugin-dialog';

import { open as upstreamOpen } from '@tauri-apps/plugin-dialog';
import type { OpenDialogOptions as UpstreamOptions } from '@tauri-apps/plugin-dialog';
export type OpenDialogOptions = Omit<UpstreamOptions, 'pickerMode' | 'fileAccessMode'>;
export type OpenDialogReturn<T extends OpenDialogOptions> = T['multiple'] extends true ? string[] | null : string | null;
export async function open<T extends OpenDialogOptions>(options?: T & { pickerMode?: never; fileAccessMode?: never }): Promise<OpenDialogReturn<T>> {
  for (const key of ['pickerMode', 'fileAccessMode']) {
    if (options && key in options) throw new TypeError(`@netwiv/api does not support dialog option '${key}'.`);
  }
  return upstreamOpen(options);
}
