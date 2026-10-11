The two vendored JavaScript modules under `vendor/@tauri-apps/api` are the
unmodified `core.js` and `external/tslib/tslib.es6.js` modules from
`@tauri-apps/api@2.11.1`, pinned by `compatibility/upstream-lock.json`.

`core.js` retains its Tauri MIT/Apache-2.0 SPDX header. `tslib.es6.js` retains
the Microsoft permission and warranty notice in its source. They are copied
only into Blazor and Rust/WASM templates, where npm package resolution is not
available. The NetWiv CLI and runtime do not require Rust/Cargo for the .NET
backend; Rust/WASM templates opt into Cargo for their frontend.
