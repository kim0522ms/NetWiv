export { save, message, ask, confirm } from '../upstream/plugins/dialog.js';
export type { MessageDialogDefaultButtons, MessageDialogButtonsYesNoCancel, MessageDialogButtonsOkCancel, MessageDialogButtonsOk, MessageDialogCustomButtons, MessageDialogButtons, MessageDialogResult, DialogFilter, SaveDialogOptions, MessageDialogOptions, ConfirmDialogOptions } from '../upstream/plugins/dialog.js';
import type { OpenDialogOptions as UpstreamOptions } from '../upstream/plugins/dialog.js';
export type OpenDialogOptions = Omit<UpstreamOptions, 'pickerMode' | 'fileAccessMode'>;
export type OpenDialogReturn<T extends OpenDialogOptions> = T['multiple'] extends true ? string[] | null : string | null;
export declare function open<T extends OpenDialogOptions>(options?: T & {
    pickerMode?: never;
    fileAccessMode?: never;
}): Promise<OpenDialogReturn<T>>;
