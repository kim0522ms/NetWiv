# macOS sleeping-display diagnostic

On 2026-10-13, the full desktop smoke failed `native monitor geometry` on a
physical Apple M1 Mac mini with macOS 27.0 beta (26A5388g), Xcode 27,
.NET SDK 8.0.406/runtime 8.0.13 and Rust 1.95.0. The logged-in console had an
online Q90A display, reported as 3840x2160 with a 1920x1080 desktop, but
`system_profiler SPDisplaysDataType` also reported `Display Asleep: Yes`.

The unchanged Tao 0.36.0 macOS implementation obtains `available_monitors()`
from `CGGetActiveDisplayList`. The standalone [CoreGraphics diagnostic](display.c)
returned `active error=0 count=0`, `online error=0 count=1`, `main=1` while the
display was asleep. No NetWiv filtering or monitor deserialization defect was
found. The assertion requires a nonempty list with positive width and scale.

After a temporary `caffeinate -u -t 120` wake, the same diagnostic returned:

```text
active error=0 count=1
id=1 online=1 active=1 asleep=0 pixels=1920x1080 bounds=0,0 1920x1080
online error=0 count=1
main=1
```

The unchanged managed executable then passed the entire ordinary smoke, exit 0.
An initial AOT rerun encountered the sleeping-display condition again; its
[failure log](full-aot-asleep.log) is retained. Executing AOT directly under
`caffeinate -d -u -t 120` passed the entire ordinary smoke, exit 0, and the
postrun diagnostic again reported one active, awake display.

Both published executables used source
`c76f5ccf08748d48fad266e4f7158319e5ba5c86`; the unchanged native source was
`ab6a39bff4af80c8345235322c22ede2fe943594`. The artifacts were built with
`CARGO_PROFILE_RELEASE_STRIP=none` to avoid the separate macOS 27 Mach-O
string-pool stripping failure. SHA-256:

| Artifact | SHA-256 |
| --- | --- |
| Managed apphost | `2f0b74799136514bfa232b5bb85273e564f604e03f950b5652f1bd317d1ee9bc` |
| NativeAOT executable | `32b7a8e8455174b4fbe5de62e3737142dbba4b4da4bbf55cb2e6554db04cb690` |
| Native dylib in both outputs | `8614c6e8dc311bcac18f532eccd24df5d81f5c425610842d7c057825c41b0f6c` |

Procedure, with `PUBLISH_ROOT` pointing to those managed/AOT output directories
and `DOTNET_ROOT` pointing to the isolated .NET 8 installation:

```sh
cc display.c -framework ApplicationServices -o display
./display
caffeinate -d -u -t 120 env NETWIV_SMOKE_HOLD_MS=0 \
  "$PUBLISH_ROOT/managed/NetWiv.Desktop.Smoke"
caffeinate -d -u -t 120 env NETWIV_SMOKE_HOLD_MS=0 \
  "$PUBLISH_ROOT/aot/NetWiv.Desktop.Smoke"
./display
```

[Managed output](full-managed.log) and [AOT output](full-aot.log) cover the
existing ordinary smoke assertions, including monitor geometry, guest IPC,
data stores, window reparenting/lifecycles, reload invalidation and resource
cleanup. No production code or assertion was changed. These are automated
application runs in a real logged-in desktop session; no screenshot or manual
visual interaction was captured. Intel, minimum macOS 12, multiple displays,
and complete upstream conformance remain unverified by this diagnostic.
