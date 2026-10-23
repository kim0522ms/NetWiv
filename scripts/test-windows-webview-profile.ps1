param(
    [Parameter(Mandatory = $true)][string]$ManagedDirectory,
    [Parameter(Mandatory = $true)][string]$AotDirectory
)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'This check requires Windows.' }
$managed = (Resolve-Path -LiteralPath $ManagedDirectory).Path
$aot = (Resolve-Path -LiteralPath $AotDirectory).Path
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../artifacts'))
$work = Join-Path $artifactRoot ('webview-profile-' + [guid]::NewGuid().ToString('N'))
$first = Join-Path $work 'first-app'
$second = Join-Path $work 'second-app'
$localData = [Environment]::GetFolderPath('LocalApplicationData')
$identifier = 'dev.netwiv.profile.' + [guid]::NewGuid().ToString('N')
$otherIdentifier = $identifier + '.other'
$relative = 'netwiv-profile-' + [guid]::NewGuid().ToString('N')
$explicit = Join-Path $work 'explicit-profile'
$ownedProfiles = @((Join-Path $localData $identifier), (Join-Path $localData $otherIdentifier), (Join-Path $localData "main/$relative"))

function Remove-TestDirectory([string]$Path, [string]$Parent) {
    $full = [IO.Path]::GetFullPath($Path)
    $prefix = [IO.Path]::GetFullPath($Parent).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Test cleanup target is outside its owned parent.' }
    for ($attempt = 0; $attempt -lt 100; $attempt++) {
        try {
            if (Test-Path -LiteralPath $full) { Remove-Item -LiteralPath $full -Recurse -Force }
            return
        } catch { Start-Sleep -Milliseconds 100 }
    }
    Write-Warning 'A synthetic browser profile is still locked after shutdown; cleanup remains pending.'
}

function Invoke-Profile([string]$Application, [string]$Mode, [string]$Identity, [string]$Expected, [string]$Next, [string]$Directory = 'default') {
    $probeArguments = @('--webview-profile-smoke', $Identity, $Expected, $Next, $Directory)
    if ($Mode -eq 'dll') { & dotnet (Join-Path $Application 'NetWiv.Desktop.Smoke.dll') @probeArguments }
    elseif ($Mode -eq 'aot') { & (Join-Path $Application 'NetWiv.Desktop.Smoke.Aot.exe') @probeArguments }
    else { & (Join-Path $Application 'NetWiv.Desktop.Smoke.exe') @probeArguments }
    if ($LASTEXITCODE -ne 0) { throw "Browser profile assertion failed in $Mode mode." }
}

try {
    foreach ($application in @($first, $second)) {
        New-Item -ItemType Directory -Path $application -Force | Out-Null
        Get-ChildItem -LiteralPath $managed | Copy-Item -Destination $application -Recurse
        Copy-Item -LiteralPath (Join-Path $aot 'NetWiv.Desktop.Smoke.exe') -Destination (Join-Path $application 'NetWiv.Desktop.Smoke.Aot.exe')
        # These are the probe's own default profiles, derived only for cleanup.
        $identity = "NETWIV.DESKTOP.SMOKE`n" + [IO.Path]::GetFullPath($application).TrimEnd('\', '/').ToUpperInvariant()
        $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($identity)))
        $ownedProfiles += Join-Path $localData "com.netwiv.app/webview/$digest"
    }
    Invoke-Profile $first dll $identifier empty managed
    Invoke-Profile $first apphost $identifier managed apphost null
    Invoke-Profile $first aot $identifier apphost aot
    Invoke-Profile $second dll $identifier aot moved
    Invoke-Profile $second apphost $otherIdentifier empty isolated
    Write-Output 'PASS configured identity persists across DLL, apphost, NativeAOT and deployment locations; other identities are isolated'

    Invoke-Profile $first dll unconfigured empty default-dll
    Invoke-Profile $first apphost unconfigured default-dll default-apphost
    Invoke-Profile $first aot unconfigured default-apphost default-aot
    Invoke-Profile $first apphost COM.NETWIV.APP default-aot default-reserved
    Invoke-Profile ($first.ToUpperInvariant()) apphost unconfigured default-reserved default-case
    Invoke-Profile $second dll unconfigured empty separate
    Invoke-Profile $first dll unconfigured default-case original
    Write-Output 'PASS unconfigured applications retain state within an application and isolate separate application locations'

    Invoke-Profile $first dll $identifier empty explicit $explicit
    Invoke-Profile $second aot $otherIdentifier explicit shared $explicit
    Invoke-Profile $first dll $identifier moved preserved
    if (-not (Test-Path -LiteralPath $explicit -PathType Container)) { throw 'The absolute data directory was not used.' }
    Invoke-Profile $first dll $identifier empty relative $relative
    Invoke-Profile $second apphost $otherIdentifier relative relative-shared $relative
    if (-not (Test-Path -LiteralPath (Join-Path $localData "main/$relative") -PathType Container)) { throw 'The relative data directory was not resolved under LocalData/label.' }
    Write-Output 'PASS explicit absolute and relative profiles keep their existing resolution and sharing behavior'
} finally {
    foreach ($profile in $ownedProfiles) { Remove-TestDirectory $profile $localData }
    Remove-TestDirectory $work $artifactRoot
}
