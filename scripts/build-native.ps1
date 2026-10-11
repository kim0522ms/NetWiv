param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -and $PSVersionTable.PSEdition -ne 'Desktop') {
    throw 'Use scripts/build-native.sh on Linux.'
}
$repository = Split-Path $PSScriptRoot -Parent
$manifest = Join-Path $repository 'native/netwiv-native/Cargo.toml'
$targetDirectory = Join-Path $repository 'artifacts/native-target/windows'
$destination = Join-Path $repository 'artifacts/native/win-x64'
$cargoArguments = @('+1.95.0', 'build', '--locked', '--manifest-path', $manifest,
    '--target', 'x86_64-pc-windows-msvc', '--target-dir', $targetDirectory)
if ($Configuration -eq 'Release') { $cargoArguments += '--release' }
& cargo @cargoArguments
if ($LASTEXITCODE -ne 0) { throw "Native build failed with exit code $LASTEXITCODE." }
$profile = $Configuration.ToLowerInvariant()
$library = Join-Path $targetDirectory "x86_64-pc-windows-msvc/$profile/netwiv_native.dll"
$loader = Get-ChildItem -Path (Join-Path $targetDirectory "x86_64-pc-windows-msvc/$profile/build/webview2-com-sys-*/out/x64/WebView2Loader.dll") |
    Select-Object -First 1 -ExpandProperty FullName
if (-not $loader) { throw 'The matching x64 WebView2Loader.dll was not produced by webview2-com-sys.' }
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item -LiteralPath $library -Destination $destination
Copy-Item -LiteralPath $loader -Destination $destination
$digest = (Get-FileHash -Algorithm SHA256 -LiteralPath $library).Hash.ToLowerInvariant()
Write-Output "win-x64 netwiv_native.dll SHA256 $digest"
