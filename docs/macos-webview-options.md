# macOS WebView creation options

NetWiv passes these creation options to its pinned Wry 0.55.1 backend for both
root and child WKWebViews. They cannot be changed on an existing WebView.

| Option | Default | macOS behavior | Version boundary |
| --- | --- | --- | --- |
| `acceptFirstMouse` | `false` | The WebView answers AppKit's `acceptsFirstMouse:` with the requested value. With `true`, a click in an inactive window can both activate it and reach the page. With `false`, the activation click is consumed. | Supported within the macOS 12+ deployment baseline. |
| `allowLinkPreview` | `true` | Sets WKWebView's `allowsLinkPreview`. Preview presentation also depends on the available system gesture and page/link. | Supported within the macOS 12+ deployment baseline. |
| `backgroundThrottling` | WebKit default when omitted | Sets WKPreferences' `inactiveSchedulingPolicy`: `disabled` selects `None`, `suspend` selects `Suspend`, and `throttle` selects `Throttle`. | macOS 14+. On macOS 12/13, Wry accepts the option and leaves WebKit's default policy unchanged. |

An inactive scheduling policy concerns a WebView outside a window. Hiding,
minimizing or occluding a window does not promise that WebKit treats its WebView
as detached. `disabled` does not guarantee a timer frequency or that work
continues during process suspension or system sleep. Applications should not
use a hidden page as a reliable scheduler.

The mappings match the
[pinned Tauri runtime source](https://github.com/tauri-apps/tauri/blob/7cd71369c00978a3783b6ae3e9972358abbe4ae6/crates/tauri-runtime-wry/src/lib.rs)
and unchanged `@tauri-apps/api@2.11.1` option declarations. This source comparison
does not establish differential gesture or timing conformance.

## Functional checks

After building the native adapter on macOS, publish and execute both consumers:

```sh
dotnet publish tests/NetWiv.Desktop.Smoke -c Release -r osx-arm64 -p:PublishAot=false -o artifacts/webview-options/managed
dotnet artifacts/webview-options/managed/NetWiv.Desktop.Smoke.dll --webview-options-smoke
dotnet publish tests/NetWiv.Desktop.Smoke -c Release -r osx-arm64 -o artifacts/webview-options/aot
artifacts/webview-options/aot/NetWiv.Desktop.Smoke --webview-options-smoke
```

The focused probe calls the live WKWebView's
`acceptsFirstMouse:` method, reads `allowsLinkPreview`, and reads its actual
configuration's `inactiveSchedulingPolicy`. It covers initial windows, dynamic
root and child WebViews, both boolean values, omitted defaults, all three
policies, and destruction of the dynamic windows. It asserts WebKit state rather
than echoing parsed configuration or treating successful creation as proof.
Below macOS 14 the scheduling assertions are explicitly skipped; successful
creation on an older system alone does not prove policy equivalence.

These assertions do not exercise real pointer input, preview presentation, or
background task timing. For a separate input check, run the published executable
with `--webview-options-input-smoke`. It first performs the same assertions, then
holds two windows for two minutes:

1. Activate the `NetWiv first mouse ON` window by its title bar. Click the OFF
   window's button once: its counter should remain `0`. Click it again: `1`.
2. Activate the OFF window by its title bar. Click the ON window's button once:
   its counter should become `1`.
3. Where the system offers a link-preview gesture, compare the test link with
   preview enabled in ON and disabled in OFF. A normal link click navigates and
   is not a preview test.

The input mode's exit status covers automatic assertions only. A tester must
describe the actual input and observed result separately; waiting for the
two-minute hold does not mark those interactions as passed. Remote pointer input
on physical macOS arm64 confirmed the OFF activation click stayed at zero, the
second click reached one, and the ON activation click reached one in both managed
and NativeAOT execution. This does not establish pressure-based preview gestures.
macOS 12/13,
preview gestures and background timing each need their own execution coverage.

See the [macOS guide](macos.md) for build prerequisites and the broader validation
boundary.
