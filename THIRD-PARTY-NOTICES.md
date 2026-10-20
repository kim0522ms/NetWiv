# Third-party notices

NetWiv is licensed under Apache-2.0. Dependencies retain their respective
licenses; this file does not relicense them.

| Component | Origin | License / notice location |
| --- | --- | --- |
| Microsoft.AspNetCore.Components.WebView 8.0.32, reused by optional NetWiv.BlazorHybrid | https://github.com/dotnet/aspnetcore/tree/v8.0.32/src/Components/WebView/WebView | MIT; NuGet package license; upstream renderer and embedded JavaScript are used without modification |
| ASP.NET Core 8.0.32 render-batch serialization, DOM event readers, navigation and WebView renderer shell, adapted in `src/NetWiv.BlazorHybrid/Aot/Hosting` | https://github.com/dotnet/aspnetcore/tree/7413d24330774263a78a4f3837ca53493518455a/src/Components | MIT; `licenses/dotnet-runtime-MIT.txt`, included in NetWiv.BlazorHybrid; exact source origins and adaptations in `src/NetWiv.BlazorHybrid/Aot/Hosting/README.md` |
| TAURI® API, configuration schema, permission/API declarations and reference runtime | https://github.com/tauri-apps/tauri | MIT OR Apache-2.0; pinned source in `compatibility/upstream-lock.json`; copied API license texts accompany its JavaScript |
| Tauri desktop plugin guests, privately bundled in `@netwiv/api` | https://github.com/tauri-apps/plugins-workspace | MIT OR Apache-2.0; pins in `compatibility/upstream-lock.json`, notices in `packages/api/THIRD-PARTY-NOTICES.md` |
| wry | https://github.com/tauri-apps/wry | MIT OR Apache-2.0 |
| tao | https://github.com/tauri-apps/tao | MIT OR Apache-2.0 |
| muda | https://github.com/tauri-apps/muda | MIT OR Apache-2.0 |
| tray-icon | https://github.com/tauri-apps/tray-icon | MIT OR Apache-2.0 |
| glob 0.3.4 matching rules, adapted in `LabelPattern.cs` | https://github.com/rust-lang/glob | MIT OR Apache-2.0; copyright and MIT grant below |
| http-range 0.1.5 range parsing rules, adapted in `FileAssetProtocol.cs` | https://github.com/bancek/rust-http-range | MIT; full grant below |
| infer 0.19.0 file signatures, adapted in `FileAssetMimeType.cs` | https://github.com/bojand/infer | MIT; full grant below |
| Json5 source | See `src/NetWiv.Build/ThirdParty/Json5/SOURCE.md` | MIT; original `LICENSE.txt` preserved alongside source |
| Tomlyn | https://github.com/xoofx/Tomlyn | BSD-2-Clause; NuGet package license |
| AngleSharp 1.5.2 | https://github.com/AngleSharp/AngleSharp | MIT; `licenses/AngleSharp-MIT.txt`, included in NetWiv.Core |
| JetBrains Mono 2.304 glyph outlines, in `assets/brand/netwiv-banner-*.svg` and `assets/diagrams/architecture-*.svg` | https://github.com/JetBrains/JetBrainsMono | OFL-1.1; Copyright 2020 The JetBrains Mono Project Authors; `licenses/JetBrainsMono-OFL.txt` |
| Roboto 2.000980 glyph outlines, in `assets/brand/netwiv-banner-*.svg` and `assets/diagrams/architecture-*.svg` | https://github.com/googlefonts/roboto | Apache-2.0; font data copyright Google 2014; `LICENSE-APACHE` |
| .NET 10 JSON naming policies | https://github.com/dotnet/runtime/tree/v10.0.0/src/libraries/System.Text.Json/Common | MIT; adapted in NetWiv.Generators/JsonWireNames.cs; `licenses/dotnet-runtime-MIT.txt`, included in NetWiv.Generators |

The native Cargo.lock records exact transitive dependencies and registry
checksums. Release packaging must include the license notices from that resolved
graph and from the selected NuGet packages, as well as this document. The npm
wrapper's external native tools have their own notices and integrity manifest.

## glob

Copyright (c) 2014 The Rust Project Developers

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.

## Tauri reused source

Copyright 2016-2019 Cargo-Bundle developers <https://github.com/burtonageo/cargo-bundle> (bundle categories).

Copyright 2019-2024 Tauri Programme within The Commons Conservancy.

MIT License

Copyright (c) 2017 - Present Tauri Apps Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.


## http-range-0.1.5

MIT License

Copyright (c) 2016 Luka Zakrajšek

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.


## infer-0.19.0

MIT License

Copyright (c) 2019 Bojan

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.


## cfb 0.7.3

MIT License

Copyright (c) 2017 Matthew D. Steele

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.


## urlpattern 0.3.0 (C# port and comparison fixtures)

MIT License

Copyright (c) 2021 the Deno authors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.
WRY and TAO are trademarks of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.
