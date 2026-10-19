import { TauriEvent as UpstreamEvent } from '@tauri-apps/api/event';
export { listen, once, emit, emitTo } from '@tauri-apps/api/event';
export type {
  Event, EventTarget, EventCallback, UnlistenFn, EventName, Options
} from '@tauri-apps/api/event';

/** Desktop event names retain their existing wire values. */
export const TauriEvent = Object.freeze({
  WINDOW_RESIZED: UpstreamEvent.WINDOW_RESIZED,
  WINDOW_MOVED: UpstreamEvent.WINDOW_MOVED,
  WINDOW_CLOSE_REQUESTED: UpstreamEvent.WINDOW_CLOSE_REQUESTED,
  WINDOW_DESTROYED: UpstreamEvent.WINDOW_DESTROYED,
  WINDOW_FOCUS: UpstreamEvent.WINDOW_FOCUS,
  WINDOW_BLUR: UpstreamEvent.WINDOW_BLUR,
  WINDOW_SCALE_FACTOR_CHANGED: UpstreamEvent.WINDOW_SCALE_FACTOR_CHANGED,
  WINDOW_THEME_CHANGED: UpstreamEvent.WINDOW_THEME_CHANGED,
  WINDOW_CREATED: UpstreamEvent.WINDOW_CREATED,
  WEBVIEW_CREATED: UpstreamEvent.WEBVIEW_CREATED,
  DRAG_ENTER: UpstreamEvent.DRAG_ENTER,
  DRAG_OVER: UpstreamEvent.DRAG_OVER,
  DRAG_DROP: UpstreamEvent.DRAG_DROP,
  DRAG_LEAVE: UpstreamEvent.DRAG_LEAVE
});
export type TauriEvent = typeof TauriEvent[keyof typeof TauriEvent];
