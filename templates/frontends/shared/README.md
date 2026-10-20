The standalone NetWiv modules under `vendor/@netwiv/api` are generated from
`packages/api/lib` by `scripts/generate-frontend-template-overlays.mjs`.
They carry the same private core/plugin implementations as the npm facade.
The Blazor starter uses these relative ES modules without npm resolution.
Upstream implementation paths are private; application imports use the facade.

Original copyright/SPDX headers are retained, including the Microsoft tslib
permission and warranty notice. See `vendor/@netwiv/api/THIRD-PARTY-NOTICES.md`
and the accompanying license texts for sources, versions and modifications.

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.
