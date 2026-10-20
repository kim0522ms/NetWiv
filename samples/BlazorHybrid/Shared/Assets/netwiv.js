// The generated C# client passes opaque JSON through the public NetWiv facade.
export { invokeJson } from './vendor/@netwiv/api/core.js';
import { getCurrentWindow } from './vendor/@netwiv/api/window.js';

export function executionMode() {
    return document.documentElement.dataset.blazorMode;
}

export function readDocumentTitle() {
    return document.title;
}

export function reload() {
    window.location.reload();
}

export function closeWindow() {
    return getCurrentWindow().close();
}
