# NetWiv + React

A NetWiv desktop app with a React 19 + TypeScript + Vite 8 frontend and a C#
backend. It needs the .NET 8 SDK or later and Node.js 20.19+ or 22.12+. Windows
uses the WebView2 Runtime, Linux uses GTK3 and WebKitGTK 4.1, and macOS 12 or
later uses the system WKWebView and AppKit. Rust is not required.

`@netwiv/api` installs from the bundled `vendor/netwiv-api` folder, so no npm
registry release is needed. Core modules are `@netwiv/api/*` and plugin modules
are `@netwiv/api/plugins/*`.

`src-netwiv/icons` contains the default NetWiv PNG, ICO and ICNS artwork selected
by `bundle.icon`. Replace these files with your application's icons. The PNG is
embedded for runtime window/Dock icons; ICO and ICNS serve Windows executables
and macOS app bundles respectively.

Add your NetWiv package folder to the project's `NuGet.Config`. Every NetWiv
package must match the `NetWivVersion` in `src-netwiv/ReactExample.csproj`. You
can also create this project from the template package:

```powershell
dotnet new install /path/to/NetWiv.Templates.nupkg
dotnet new netwiv-react -n MyApp --NetWivVersion 0.1.0-dev
```

## Run it

```powershell
npm ci
npm run desktop:dev
```

This restores the pinned `NetWiv.Cli` tool, generates `src/generated/commands.js`
and `commands.d.ts` from your C# commands, starts Vite and opens the app window.
Type a name and press **Call greet**: the call count comes from the C#
`State<GreetingCounter>`. React and CSS edits appear through Vite; C# edits
regenerate the bindings and restart the app. Stop with Ctrl+C.

`npm run dev` previews the page in a browser only. A plain browser has no native
bridge, so the page says it is not in a NetWiv window and C# calls fail. To
attach the app to a Vite server you started yourself, run
`npm run desktop:dev:backend`. Vite is fixed to `127.0.0.1:5173` and fails
instead of picking another port; the app accepts that origin only in Debug with
`--dev`.

In Rider or Visual Studio, open `ReactExample.sln` and run the C# project
`ReactExample`. MSBuild restores npm packages, generates the bindings, builds
React and embeds the assets; Node.js and npm must be on the IDE's PATH. This path
uses the built frontend, so rebuild after changing the screen. To build only the
frontend, run `dotnet build ReactExample.Frontend.esproj`. The `.esproj` uses
the MIT-licensed `Microsoft.Build.NoTargets/3.7.134` SDK, restored from NuGet on
the first build.

## The code that matters

`src/App.tsx`:

```tsx
import { commands } from './generated/commands.js';

const result = await commands.greet({ name });
setMessage(result.message);
```

`src-netwiv/Program.cs`:

```csharp
[Command]
public static Greeting Greet(string name, State<GreetingCounter> counter)
    => new($"Hello, {name}!", counter.Value.Next());
```

Commands are registered with `.InvokeHandler("greet")`, and
`[assembly: GenerateFrontendBindings]` turns on the JavaScript and TypeScript
generation. When you add a return type, add `[JsonSerializable(typeof(MyDto))]` to
`AppJsonContext`. Do not edit the generated files. The starter screen and its
styles are in `src/App.tsx` and `src/style.css`; replace them with your own.

## Publish

```powershell
dotnet tool restore
dotnet tool run netwiv -- build
```

Self-contained .NET is the default. Add `--aot` for NativeAOT or
`--framework-dependent` for an installed .NET runtime. Distribute the whole
publish folder, including the native library. The built frontend is embedded in
the executable, so the published app does not need Node.js or Vite.

Windows NativeAOT builds need the C++ build tools and the Windows SDK. Build Linux
packages on Linux with GTK3 and WebKitGTK 4.1 installed. macOS supports Apple
Silicon (`osx-arm64`) and needs the Xcode command line tools for NativeAOT; keep
the generated synchronous `Main`, because AppKit runs on the main thread. To make
an `.app` and `.dmg`:

```sh
dotnet tool run netwiv -- build --target osx-arm64 --bundles app,dmg --no-sign
```

The output is in `target/osx-arm64/release/bundle/`. `--no-sign` still renews the
ad-hoc signature the app needs to run; it does not provide Developer ID trust.

## Smoke test

```powershell
npm run smoke
```

The app opens with `?smoke=1`, the page submits its form once, and the C#
`smoke_done` command exits with the result: a real form submission through
`@netwiv/api`, the generated binding, the C# command and its JSON response. The
command is registered only for `--smoke-test` runs.

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.
