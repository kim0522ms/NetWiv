# Create a NetWiv app with the .NET CLI

Install `NetWiv.Cli` as a .NET tool to create applications with `netwiv new`.
It provides [18 frontend choices corresponding to the official TAURI® selector](template-coverage.md).
The default is React + Vite + C#. Ordinary JS/TS and Blazor C# backends require
neither Rust nor Cargo. Rust/WASM frontends require Cargo and Trunk.

Prepare a package feed using the [quickstart](integration-quickstart.md).
Replace `$feed` and `$version` with actual values, then run from the parent
directory where the new project should be created:

```powershell
$feed = 'C:\path\to\packages'
$version = 'PACKAGE_VERSION'
dotnet tool install --global NetWiv.Cli --version $version --add-source $feed

netwiv new MyApp --template react --source $feed
cd MyApp
npm ci
npm run desktop:dev
```

If the global tool is already installed, use `update` instead of `install`.
If the global tools directory is missing from PATH, Windows users can run
`& "$env:USERPROFILE\.dotnet\tools\netwiv.exe" new MyApp --source $feed`.

On Linux/macOS, use the same PowerShell commands with the actual package directory;
the [quickstart](integration-quickstart.md#create-the-project) also includes zsh commands.
Windows requires WebView2 Runtime; Linux requires GTK3 and WebKitGTK 4.1;
macOS 12+ uses system WKWebView/AppKit; NativeAOT requires Xcode Command Line Tools.
NativeAOT publication also requires the platform's native build tools.

## First build and platform bundles

The CLI checks `frontendDist/index.html` **after** MSBuild generates the frontend.
A fresh React project with restored dependencies can therefore run
`netwiv build --no-bundle` without creating `dist` in advance. Publication fails
if frontend output is still missing afterward. Existing `beforeBuildCommand`
hooks remain supported. Use the same package version for the SDK and CLI.

`netwiv build` defaults to self-contained .NET; use `--framework-dependent` for
an installed runtime or `--aot` for NativeAOT. See [deployment modes](deployment.md).

On macOS, `netwiv build --bundles app,dmg --no-sign` publishes managed .NET for the
current architecture and creates an ad-hoc-signed `.app` and a `.dmg`. Explicit
targets are `osx-arm64` and `osx-x64` (or their Apple target triples). Build on the
matching Mac. Bundles use system WKWebView; there is no separate WebView runtime
installer. See [macOS](macos.md) for configuration overlays, output paths,
Developer ID signing configuration and unimplemented notarization.

Windows NSIS/MSI installers check Evergreen WebView2 Runtime registrations
matching the installation scope: current-user packages accept user or machine
registrations; per-machine packages require a machine registration. If the runtime is absent, installation stops and directs
the user to Microsoft's installer page; interactive NSIS opens it only with user
consent. Silent NSIS fails with exit code 2. Automatic runtime download and
Fixed Version bundling are not included. MSI maintenance and removal remain
available after the runtime is removed. Checks use the registry locations in
[Microsoft's distribution guidance](https://learn.microsoft.com/microsoft-edge/webview2/concepts/distribution).
Both formats default to current-user installation and manage Start menu shortcuts;
NSIS also creates a desktop shortcut by default. See [Windows installer
configuration](windows-installers.md) for per-machine scope, directory selection,
shortcut launch arguments and upgrade/uninstall behavior.

## Creation options

```text
netwiv new NAME [-t|--template TEMPLATE] [-m|--manager MANAGER] [-o|--output DIRECTORY]
               [--identifier ID] [--source NUGET_SOURCE]
netwiv new --list
netwiv new --help
```

- `NAME`: Project, executable and application name; also the default output
  directory name.
- `--template`: Defaults to `react`. Use `netwiv new --list` or the
  [template matrix](template-coverage.md) for all choices.
- `--manager`: JS/TS accepts `npm` (default), `pnpm`, `yarn`, `bun` or `deno`;
  Rust/WASM uses `cargo`, and Blazor uses `dotnet`.
- `--output`: A new or empty directory. Existing files are not overwritten.
- `--identifier`: App identifier. Defaults to `dev.netwiv.<lowercase-project-name>`,
  with `_` replaced by `-`.
- `--source`: A local NuGet directory or HTTP(S) feed, written to the generated
  `NuGet.Config`. Omitting it does not add a project-specific feed; private
  packages then require a separately configured source.

```powershell
netwiv new MyApp --output 'C:\Projects\My App' --identifier com.example.myapp --source $feed
```

The CLI processes bundled templates in a dedicated temporary template cache.
It does not install or replace global user templates, and preserves an existing
output directory if generation fails. Project creation does not automatically
install npm dependencies or launch the app.

## Use the generated app

Write `[Command]` C# methods in `src-netwiv/Program.cs`. JS/TS frontend code calls
functions from `src/generated/commands.js`; JavaScript functions and TypeScript
declarations are generated during build and development.

`<AppName>.sln` contains the C# app and `<AppName>.Frontend.esproj`. Open the
solution and select the **C# project as the run/debug target**. Without extra
launch arguments, MSBuild prepares frontend dependencies, generates C# bindings,
builds the frontend and embeds assets. Building the `.csproj` alone follows the
same workflow. Frontend tools must be available in the IDE's PATH. For ordinary
Debug launches, rebuild and rerun after UI edits; use `desktop:dev` below for
live frontend updates.

The internal binding build uses `obj/bindings` and `bin/bindings` to separate it
from outer NuGet restore, debugging and NativeAOT outputs. Design-time and
development-server builds do not build the frontend twice.

The frontend project restores `Microsoft.Build.NoTargets/3.7.134` (MIT) from
NuGet. Microsoft's Visual Studio JavaScript SDK is not a default dependency.
Solution/project/NativeAOT/CLI builds are covered by automated validation.
Actual Rider/Visual Studio solution loading and debugging remain unverified.

```powershell
dotnet build MyApp.sln
dotnet build MyApp.Frontend.esproj
```

The `.esproj` builds the frontend once. Templates do not add a duplicate
`npm run build` to `beforeBuildCommand`; custom hooks still run normally.

```powershell
# Development: frontend updates, regenerated C# bindings and app restarts
npm run desktop:dev

# Self-contained .NET build (add --aot to opt into NativeAOT)
dotnet tool restore
dotnet tool run netwiv -- build --no-bundle
```

Full Tauri compatibility and release acceptance remain incomplete. See
[support and validation](implementation-status.md) and the
[frontend generator](frontend-generator.md).

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.
