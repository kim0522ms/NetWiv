# Desktop drag and drop

Native file drops and browser HTML drag and drop use different event paths.
Choose the path when creating the WebView with `dragDropEnabled` (default:
`true`). This option belongs to each WebView, including child WebViews.

## Native file events

Use `getCurrentWebview().onDragDropEvent` from `@netwiv/api/webview` to receive
events for the current WebView. The payload is a discriminated union:

| `payload.type` | Other fields |
| --- | --- |
| `enter` | `paths: string[]`, `position: PhysicalPosition` |
| `over` | `position: PhysicalPosition` |
| `drop` | `paths: string[]`, `position: PhysicalPosition` |
| `leave` | None |

`paths` contains native filesystem paths, rather than file URLs or browser
`File` objects. An event does not itself authorize an arbitrary filesystem
operation; the file API's permissions and scopes still apply. Non-file drags
are not guaranteed to produce native file events. Do not assume `paths` is
nonempty or index its first entry without checking.

`position` is relative to the top-left of the **receiving WebView's client
area**, in physical pixels, with x increasing right and y increasing down.
It is not relative to the desktop or parent window, and excludes the title
bar and the child's offset in its parent. The same coordinates appear in
the corresponding C# `DragEnterEvent`, `DragOverEvent` and `DragDroppedEvent`.
A native event also reaches subscribers on the receiving WebView's parent
`Window`/`WebviewWindow`, once per subscription; it does not reach sibling
WebView subscribers. C# `AppEvent.Label` identifies the source WebView even
when received through the parent window.
A leave event has no last position. Coordinates may be negative near an edge;
do not clamp them before hit testing.

NetWiv normalizes the pinned Wry 0.55.1 callbacks at its native boundary:

| Backend | Coordinate adaptation |
| --- | --- |
| Windows / WebView2 | Wry's `ScreenToClient` already produces WebView-child client physical pixels; NetWiv preserves them. |
| Linux / WebKitGTK | Wry reports widget-local logical coordinates; NetWiv applies the receiving GTK widget's current scale factor. |
| macOS / WKWebView | NetWiv reverses Wry's window-base y inversion, converts the window point into the receiving view, then uses AppKit backing conversion with the view's actual orientation. |

AppKit's backing conversion already applies the view's orientation; NetWiv
normalizes the resulting backing point to a top-left origin. See Apple's
[coordinate conversion guidance](https://developer.apple.com/library/archive/documentation/GraphicsAnimation/Conceptual/HighResolutionOSX/APIs/APIs.html)
for the distinction between view points and backing pixels.

GTK allocates WebViews through the parent container, so requested child x/y
coordinates do not imply Windows-style absolute layout. Drop coordinates are
relative to the widget GTK actually allocated; see [Linux support](linux.md).
On macOS, Wry truncates the original window point to integer logical units
before NetWiv receives it. Normalization cannot recover that discarded
precision: an edge can differ by less than one logical pixel on each axis
before backing conversion. NetWiv also rounds the normalized result to integer
physical pixels. Allow for that quantization, amplified by the backing scale,
when designing narrow targets. The conversion uses live native geometry and
scale rather than values cached at WebView creation.

This normalization corrects the GTK scale and macOS child-origin behavior of
NetWiv 0.3.2 and pinned Wry; it is NetWiv's desktop contract, not a claim that
unchanged upstream callbacks produce the same coordinates on every platform.
Apps that compensated for those older GTK/macOS callbacks should remove that
extra scaling or origin subtraction. Windows coordinates are unchanged.

## Permissions and lifetime

The frontend listener uses the core event API. Grant
`core:event:allow-listen` and `core:event:allow-unlisten` to the intended local
WebViews (or a permission set containing them). No event-emission permission is
needed. For example:

```json
{
  "identifier": "file-drop-ui",
  "webviews": ["main"],
  "permissions": [
    "core:event:allow-listen",
    "core:event:allow-unlisten",
    "core:webview:allow-webview-size"
  ]
}
```

`allow-webview-size` is needed only by the zoom-aware example below. Scope
the listener to the WebView whose document performs the hit test. A parent
window can contain several WebViews; do not treat a window-level listener's
point as belonging to an arbitrary child document. Backend C# subscriptions
do not grant frontend capabilities. Dispose their returned `IDisposable` when
the owning component is disposed.

The frontend registration returns an unlisten function. Call it when the
owning UI is disposed; registering repeatedly without cleanup creates
duplicate handlers.

## Hit testing DOM drop zones

`document.elementFromPoint` takes CSS viewport coordinates. At ordinary page
zoom, convert physical pixels with `position.x / window.devicePixelRatio`
and the corresponding y value. Read the ratio in the receiving document;
the parent window's OS scale factor is not a CSS-pixel conversion API.
Page zoom can change the relationship between CSS pixels, OS logical pixels
and `devicePixelRatio`; do not multiply or divide by a stored zoom value
again if it is already reflected by the browser's ratio.

For code that also uses `Webview.setZoom`, derive the physical-to-CSS ratio
from the current native WebView size and the document's layout viewport:

```ts
import { getCurrentWebview } from '@netwiv/api/webview';

const webview = getCurrentWebview();
const unlisten = await webview.onDragDropEvent(async ({ payload }) => {
  if (payload.type !== 'drop') return;
  const size = await webview.size();
  if (size.width === 0 || size.height === 0) return;
  const x = payload.position.x * window.innerWidth / size.width;
  const y = payload.position.y * window.innerHeight / size.height;
  const zone = document.elementFromPoint(x, y)
    ?.closest<HTMLElement>('[data-drop-zone]');
  zone?.dispatchEvent(new CustomEvent('files-dropped', {
    detail: payload.paths
  }));
});
// Call unlisten() in the component's disposal/unmount hook.
```

Do not add `scrollX`/`scrollY` or subtract the child's window position:
`elementFromPoint` already expects viewport coordinates. This example uses
the top-level document and layout page zoom; additional visual-viewport
magnification or hit testing inside an iframe need the corresponding browser
coordinate handling. A resize between
the drop and the asynchronous size query can invalidate the mapping; avoid
changing the layout while handling the drop. Native integer quantization and
layout viewport rounding also limit precision at target boundaries.

## Native versus HTML drag and drop

| Platform | `dragDropEnabled: true` | `dragDropEnabled: false` |
| --- | --- | --- |
| Windows | Native file events provide paths. Wry replaces WebView2's drop target and disables external browser drop. HTML `dragenter`/`dragover`/`drop` delivery cannot be relied on, including in-page non-file drags. | No NetWiv native file events. WebView2 handles HTML drag and drop; OS files arrive as browser `File` objects. |
| Linux | Native file events are handled through GTK signals. Coexistence and ordering with HTML drag events remain unverified; do not rely on either suppression or simultaneous delivery. | No NetWiv native file events. Browser HTML drag behavior remains subject to WebKitGTK; actual file-manager and in-page scenarios require target validation. |
| macOS | NetWiv accepts Wry's native drag callback, which suppresses the default WKWebView file-drop operation. In-page HTML drag coexistence remains unverified. | No NetWiv native file events. Selected root/child in-page HTML drags passed on WKWebView; external browser file drops still require target validation. |

Native handling is not a way to populate `<input type="file">`: the accepted
native file drop may suppress that browser default, and JavaScript cannot set
a file input to an arbitrary local path. For HTML drag targets, cancel
`dragover` with `event.preventDefault()` to allow a drop, and cancel `drop` to
avoid the browser's default navigation/open behavior where applicable.

When native file drops are enabled on Windows, use pointer-based in-page
dragging if both interactions must work in the same WebView. Merely listening
to native file events does not recreate HTML `DataTransfer` or browser events.

## Windows paths with HTML drag and drop

On Windows, an application that needs HTML-based sortable lists and native
paths for dropped files can set `dragDropEnabled: false` and call NetWiv's
additive `getFilePath(file)` extension. It accepts browser `File` objects from
an OS drop or file picker; it does not replace HTML drag events:

```ts
import { getFilePath } from '@netwiv/api/core';

dropZone.addEventListener('dragover', event => event.preventDefault());
dropZone.addEventListener('drop', async event => {
  event.preventDefault();
  const files = [...(event.dataTransfer?.files ?? [])];
  const paths = await Promise.all(files.map(file => getFilePath(file)));
  // Pass paths to the application's file handling code.
});
```

Grant `core:webview:allow-get-file-path` explicitly to the receiving document's
WebView. It is excluded from `core:default` and `core:webview:default`;
`core:webview:deny-get-file-path` overrides an allow. NetWiv checks the
capability before accessing the native file path. The request is bound to the
main document and uses a single-use expiring ticket; navigation or closing
the WebView invalidates it. `pagehide` aborts pending requests. Obtaining a
path grants no filesystem read/write permission or scope.

This is a deliberate Windows-only extension; it requires WebView2's file
additional-object support. Older runtimes without that feature, Linux,
macOS, subframes and isolation mode reject with `FilePathNotSupported`.
Constructed `File` objects, blobs, and items without a native local-file
backing reject with `FilePathUnavailable`. Directory handles and non-`File`
items are not accepted. Resolution does not read file contents or check the
path's existence, type or accessibility: a file moved or deleted after selection
can still have a resolvable path. Validate it through the file API when needed.
Check errors in application code and retain a suitable alternative workflow.
The default `dragDropEnabled: true` native interception remains unchanged.
See [frontend SDK](frontend-api.md) for the supported package entry point;
this extension is NetWiv behavior, not an upstream compatibility pass.

## Verification

Selected physical Windows and macOS checks passed in managed and NativeAOT
execution: actual file-manager drops into root and child WebViews at page zoom
1 and 1.5, CSS hit testing, parent-window delivery, and trusted in-page HTML
dragging with native handling disabled. Windows also passed actual browser
file-drop path lookup, picker selection/cancellation, HTML reordering and
native-versus-HTML file-drop separation. These checks do not establish Linux
interactive acceptance, every desktop scale, minimum-OS support or full
upstream equivalence. The platform-specific limits above still apply.

Native unit regressions cover scaling, negative positions, macOS child-origin
inversion, flipped backing coordinates, and Windows identity. The C# API tests
cover the enter/over/drop/leave union and subscription cleanup. These are
deterministic checks; they do not prove OS file-manager event delivery.
Runtime-target regressions also cover parent window delivery, frontend event
targets, duplicate prevention and exclusion of unrelated WebViews/windows.

The interactive [drag fixture](../tests/NetWiv.DragDrop.Smoke) checks the
receiving root and child WebViews and the CSS hit target. Build and run it on
the target desktop with freshly built matching native assets:

```sh
dotnet run --project tests/NetWiv.DragDrop.Smoke -c Release -p:PublishAot=false
dotnet run --project tests/NetWiv.DragDrop.Smoke -c Release -p:PublishAot=false -- --zoom
dotnet run --project tests/NetWiv.DragDrop.Smoke -c Release -p:PublishAot=false -- --html
dotnet publish tests/NetWiv.DragDrop.Smoke -c Release -r win-x64
# Repeat the interactions with the published executable.
# Use linux-x64 or osx-arm64 on those targets.
```

Create an owned synthetic `netwiv-drag-fixture.txt` and drag it from the OS
file manager into the blue zone in both windows. The native cases assert an
enter followed by a drop with the expected filename and a coordinate that
hits that zone. The status line shows native enter/over/leave counts and
positions, and HTML drag stages, to distinguish gesture initiation, delivery
and hit-testing failures. It does not display file paths. An unchanged ready
status alone is not a passing drag test. The HTML case requires an actual
in-page drag of the visible source into each zone. Repeat at the intended desktop scale and after moving
or resizing the windows. A source audit, fabricated event, successful build,
or hosted/headless run is not physical interactive acceptance. The platform
table above leaves behavior without such validation explicitly unverified.
