# NetWiv app icon

A web layer stacked on a native shell, with a woven N on the front layer.

| File | Use |
| :--- | :--- |
| `netwiv-icon-dark.svg` | Default icon and dark appearance. Also `templates/app/icon.svg`. |
| `netwiv-icon-light.svg` | Light appearance. |
| `netwiv-icon-*-small.svg` | 32 px and smaller. The woven gaps disappear at these sizes, so the N is solid. |
| `png/` | Rendered sizes. 16 and 32 come from the small sources. |
| `netwiv.ico` | Windows, 16–256 px, from the dark sources. |
| `netwiv.icns` | macOS, from the dark sources, drawn on the 824/1024 icon grid. |
| `netwiv-banner-{dark,light}.svg` | README header. Text is outlined from JetBrains Mono (OFL-1.1) and Roboto (Apache-2.0), so it does not depend on installed fonts. |

Colors: `#512BD4`, `#B9A8F5`, `#17142A`, `#F6F5FB` and `#FFFFFF`. The SVGs avoid
transforms and `paint-order` so Linux desktop icon renderers (librsvg) draw
them the same way browsers do.

Regenerate the PNGs from the SVGs, for example with
`rsvg-convert -w 256 -h 256 netwiv-icon-dark.svg -o png/netwiv-icon-dark-256.png`.
