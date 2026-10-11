# Create a React app from packages

Use the NetWiv CLI to create a desktop application with a React frontend and
C# backend. You currently need a separately supplied NuGet feed or one built
from source. Cloning the repository does not provide a prebuilt package feed.
To create packages yourself, see [framework development](development.md#create-a-local-package-feed).
Full Tauri compatibility and release validation remain incomplete.

## Prerequisites

- Windows: .NET 10 SDK, WebView2 Runtime and a Node.js version supported by the
  template. The React template declares Node.js `^20.19.0 || >=22.12.0`.
- Windows NativeAOT builds: Visual Studio C++ Build Tools and Windows SDK.
- Linux: .NET 10 SDK, GTK3, WebKitGTK 4.1, Node.js and NativeAOT build dependencies.
  Build Linux distributions on a Linux host.
- macOS 12+: .NET 10 SDK, Xcode Command Line Tools and the template's Node.js
  version. WKWebView/AppKit are supplied by macOS. Use SDK and native package
  assets for the Mac's architecture; see the [macOS guide](macos.md).
- A feed containing matching versions of `NetWiv.Cli`, the SDK and native runtime
  packages. Ordinary React app development does not require Cargo.

## Create the project

The following example uses PowerShell. Replace `$feed` and `$version` with your
actual package directory and version, then run from the parent directory in
which you want to create the project. `PACKAGE_VERSION` is a placeholder, not an
installable version.

```powershell
$feed = 'C:\path\to\packages'
$version = 'PACKAGE_VERSION'

dotnet tool install --global NetWiv.Cli --version $version --add-source $feed
netwiv new MyNetWivApp --template react --source $feed
Set-Location MyNetWivApp

npm ci
npm run desktop:dev
```

If the global tool is already installed, use `update` instead of `install`.
On Linux/macOS, the same PowerShell commands work with the corresponding feed path.
For macOS Terminal (zsh), use:

```sh
feed='/absolute/path/to/packages'
version='PACKAGE_VERSION'
dotnet tool install --global NetWiv.Cli --version "$version" --add-source "$feed"
export PATH="$HOME/.dotnet/tools:$PATH"
netwiv new MyNetWivApp --template react --source "$feed"
cd MyNetWivApp
npm ci
npm run desktop:dev
```

`netwiv new` configures SDK references, the version in `.config/dotnet-tools.json`
and the NuGet feed. See [creation options](project-creation.md#creation-options)
to change the name, output directory or identifier.

`desktop:dev` restores the local CLI, generates bindings, starts Vite and launches
the app. React edits update the frontend; C# edits regenerate bindings and
restart the backend. Press Ctrl+C in the starting terminal to stop the session.

## Connect C# and React

`[Command]` methods in `src-netwiv/Program.cs` implement the backend.
`.InvokeHandler("greet")` selects the exposed command. React calls the generated
function:

```jsx
import { commands } from './generated/commands.js';

const result = await commands.greet({ name: 'NetWiv' });
```

`src/generated/commands.js` and `commands.d.ts` are generated during the build;
do not edit them manually. When adding a JSON DTO, add `JsonSerializable` to the
C# `AppJsonContext`. Unsupported custom serialization produces a build error
rather than a guessed type.

TypeScript names such as `GreetingOutput` derive from C# type names. See the
[generator reference](frontend-generator.md) for serialization support and
[DTO naming](frontend-generator.md#readable-dto-names). Permissions, command
cancellation and resource ownership are described in the [C# SDK](csharp-api.md).

## Solutions and IDEs

The generated `MyNetWivApp.slnx` includes the C# project and a `.Frontend.esproj`.
Use the C# project as the IDE launch target. See
[using the generated app](project-creation.md#use-the-generated-app) for build
integration and development-server workflows. Actual Rider/Visual Studio solution
loading and debugging validation remain incomplete.

## Publish an executable

Run from the project root:

```powershell
npm run build
dotnet publish src-netwiv -c Release -r win-x64 -p:PublishAot=true -o publish/win-x64
& ./publish/win-x64/MyNetWivApp.exe
```

Distribute the **complete publish directory**, including the executable and
native DLL/SO/dylib files. On Linux, publish for `linux-x64` and use that output's executable.
On macOS, publish for `osx-arm64` (Apple Silicon) or `osx-x64` (Intel) on a
matching Mac. Keep the generated synchronous `Main` entry point: AppKit's event
loop must run on the process main thread.

For a macOS app bundle and disk image, use the CLI from the project root:

```sh
dotnet tool restore
dotnet tool run netwiv -- build --target osx-arm64 --bundles app,dmg --no-sign
```

The outputs are under `target/osx-arm64/release/bundle/`. Use
`osx-x64` on Intel. `--no-sign` still renews the required ad-hoc executable
signature; it does not provide Developer ID trust. Read the
[signing and distribution limits](macos.md#bundles-and-signing) before distribution.

The deployed app does not need Node.js or Vite, but still requires the OS WebView
dependencies. See [support and validation](implementation-status.md) for installer
and signing coverage.

## Next steps

- Choose another frontend from the [template matrix](template-coverage.md).
- Read [plugin configuration](plugin-configuration.md) and
  [feature limits](official-plugins.md), then register only the plugins you need.
