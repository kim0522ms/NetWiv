# Windows installer configuration

NetWiv generates Windows x64 NSIS (`nsis`) and WiX v3 MSI (`msi`) installers.
Build these on Windows, with NSIS 3.11 or later and WiX v3 (including
`WixUIExtension` for directory selection) on `PATH`. These options apply equally
to managed and NativeAOT applications; see [deployment modes](deployment.md).

The following **NetWiv-specific extension** is shared by both formats. It is not
an upstream TAURI® configuration contract. The CLI validates its types and keys;
[the extension schema](windows-installer.schema.json) describes this object for
editors. Do not add these keys to the generated upstream compatibility schema.

```json
{
  "bundle": {
    "active": true,
    "targets": ["nsis", "msi"],
    "windows": {
      "installer": {
        "scope": "perMachine",
        "allowDirectorySelection": true,
        "shortcuts": {
          "name": "My App",
          "arguments": ["--profile", "Production Line 1", ""],
          "workingDirectory": ".",
          "desktop": true,
          "startMenu": true
        }
      }
    }
  }
}
```

## Options and defaults

| Key under `bundle.windows.installer` | Default | Meaning |
| --- | --- | --- |
| `scope` | `"currentUser"` | `"currentUser"` or `"perMachine"`; fixed when the package is built. |
| `allowDirectorySelection` | `false` | Show the NSIS directory page / MSI install-directory wizard. |
| `shortcuts.name` | `productName` | Display name for both shortcuts, without `.lnk`. |
| `shortcuts.arguments` | `[]` | Literal argument array; supports spaces, Unicode, quotes, empty strings and trailing backslashes. No shell expansion. |
| `shortcuts.workingDirectory` | `"."` | Directory relative to the installation root, at most 1000 UTF-16 code units. Created during installation if absent. |
| `shortcuts.desktop` | NSIS: `true`; MSI: `false` | Create a desktop shortcut. Omit to retain the format's previous default. |
| `shortcuts.startMenu` | `true` | Create a shortcut in the product's Start menu folder. |

The working directory must stay inside the install root: absolute paths, drive
prefixes, `..`, empty segments and invalid Windows names are rejected. Shortcut
names must be valid Windows filenames (up to 200 characters for NSIS, 115 for
MSI including its generated short-name allowance). Argument strings cannot contain control
characters; their quoted command line must fit 1000 UTF-16 code units (the
standard NSIS string-buffer limit). MSI carries the command through a property
so the shortcut table's 255-character argument field does not truncate it. NSIS and MSI/WiX metacharacters are escaped
independently of Windows argument quoting. Do not put shell syntax or installer
variables in the array expecting expansion: `$INSTDIR` and `[INSTALLDIR]`, for
example, are passed literally to the app.

Windows Shell expands `%NAME%` in shortcut arguments. To preserve the literal
argument contract, configurations containing more than one percent sign across
all arguments are rejected; a single percent sign (for example `50%`) is allowed.

NSIS rejects installation before copying files when the root plus the longest
generated path (payload, executable, uninstaller or working directory) exceeds
1000 UTF-16 code units.

Unknown keys, nulls, wrong types, unknown scopes, and launch settings with all
shortcuts disabled fail before build hooks, publishing or packaging tools run.
For MSI, omitting `desktop` while setting `startMenu: false` also disables all
shortcuts. Windows settings may remain in a shared cross-platform config;
Linux/macOS bundles do not use them. Platform-incompatible bundle targets fail
before build/bundle hooks run.

## Scope, upgrades and removal

| Behavior | `currentUser` | `perMachine` |
| --- | --- | --- |
| Default directory | Local application data + product folder | 64-bit Program Files + product folder |
| Installer elevation | No elevation requested | Administrator required |
| Registration | HKCU | HKLM |
| Shortcuts | Current user's desktop / Start menu | Common desktop / Start menu |
| Accepted Evergreen WebView2 registration | Current user or machine | Machine only |

Installer elevation does **not** change the installed application's manifest or
make the application request administrator rights. Supply an application
manifest through .NET/MSBuild when the app itself needs elevation.

Use a dedicated app directory to avoid file-name collisions. Both installers
remove package-owned files and empty folders on uninstall; unrelated files
inside the selected directory and application data outside it are preserved.

Subsequent installations of the same format, scope and identity recover the
stored installation directory. MSI versions predating this feature did not
store `InstallLocation` and therefore cannot recover an earlier custom path.
NSIS can recover the previous version's uninstall registration. New NSIS packages
remove the previous package-owned files before installing an upgrade, including
files omitted by the new version. Legacy packages have no ownership metadata:
their recursive uninstallers are never run automatically, so obsolete files from
those versions may require manual cleanup. Relocating a legacy NSIS installation
is rejected until it is uninstalled explicitly. New packages support relocation
and remove the old uninstaller along with the old payload. Keep the
identifier, product name and MSI `wix.upgradeCode` stable across upgrades.
Equivalent GUID spellings (case or braces) retain the same upgrade identity.
Machine MSI packages have a separate upgrade identity, so changing scope is not
a migration of an existing user installation. Uninstall the old installation
before changing scope or switching between MSI and NSIS. Both formats are
alternative distribution choices, not two installers to run for the same app.

Silent directory overrides take precedence over the recovered path:

```powershell
# /D must be last, without surrounding quotes, even when the path contains spaces.
.\MyApp-setup.exe /S /D=C:\Apps\My App
msiexec /i MyApp.msi /qn INSTALLDIR="C:\Apps\My App"
```

Omit the override on the next install to reuse that path. Directory selection is
opt-in for interactive users; silent overrides do not require an interactive page.

The WebView2 check does not download or install a runtime. If it is absent, NSIS
returns code 2 in silent mode; MSI fails its launch condition. Interactive NSIS
can open Microsoft's download page with consent. Uninstallation remains possible
when WebView2 has subsequently been removed. Framework-dependent apps also need
the corresponding .NET runtime installed separately.

## Verification

Cross-platform CLI tests validate config failures, argument quoting and both
installer renderers. `scripts/test-windows-installers.ps1` runs on a disposable,
elevated Windows CI runner: real NSIS/WiX compilation, both scopes, rejection of
missing/user-only prerequisites, actual shortcut launches recording arguments
and working directory, custom-path upgrade, and uninstall preserving unrelated and external
application data. It uses a small NativeAOT probe; WebView GUI smoke is a separate
CI gate. Interactive directory browsing and a standard user's UAC consent flow
still require manual acceptance; silent elevated CI does not prove those flows.

For local acceptance on a dedicated Windows validation machine, install NSIS 3.11,
WiX 3.14.1, .NET 8 SDK and the Visual C++ build tools, then run from an elevated
PowerShell 7 terminal with the packaging tools on `PATH`:

```powershell
dotnet build src/NetWiv.Cli -c Release
./scripts/test-windows-installers.ps1 -AllowLocalMachine
```

The explicit local flag is required outside CI. The test temporarily changes the
WebView2 `pv` values, then restores their original value/type and removes registry
keys created by the test, including on failure. Use a dedicated test machine.

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.
NetWiv is an independent project and is not affiliated with or endorsed by it.
