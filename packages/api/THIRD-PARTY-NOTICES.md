# Third-party notices

`@netwiv/api` delegates desktop API operations to the unchanged
`@tauri-apps/api@2.11.1` npm dependency. The dependency is distributed separately
under Apache-2.0 OR MIT. Its wire names and selected API names are retained for
interoperability. The facade source is maintained by NetWiv contributors under
Apache-2.0.

The distribution also includes declaration-only copies from that exact version
under `lib/upstream`, with relative import/export specifiers normalized to `.js`
for NodeNext resolution. Runtime dependency files are unchanged. Public exports
are selected by the NetWiv facade; copied internal declarations are not package
export subpaths.

Source: https://github.com/tauri-apps/tauri/tree/6f6ab1207bb3923c2721fbc67d2fdb1c8deb0c7a/packages/api

Copyright (c) 2017 - Present Tauri Apps Contributors

Copyright 2019-2024 Tauri Programme within The Commons Conservancy.

See `LICENSE-TAURI-APACHE` and `LICENSE-TAURI-MIT` for the upstream license texts.

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.
NetWiv is independent and is not affiliated with, endorsed by, or officially
approved by The Tauri Programme within the Commons Conservancy.
