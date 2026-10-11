param([Parameter(Mandatory = $true)][string]$PublishedDirectory)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$published = (Resolve-Path -LiteralPath $PublishedDirectory).Path
$binary = Join-Path $published 'NetWiv.Desktop.Smoke.exe'
if (!(Test-Path -LiteralPath $binary)) { throw "NativeAOT smoke executable is missing: $binary" }
$output = Join-Path $root ('artifacts/native-startup-win-' + [guid]::NewGuid().ToString('N'))
$cases = @{
    'missing-library' = 'native library or an OS dependency is missing'
    'wrong-version' = 'native ABI 99 does not match managed ABI 2'
    'missing-entry' = 'incompatible ABI or architecture'
}
foreach ($case in $cases.GetEnumerator()) {
    $directory = Join-Path $output $case.Key
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $candidate = Join-Path $directory 'NetWiv.Desktop.Smoke.exe'
    Copy-Item -LiteralPath $binary -Destination $candidate
    if ($case.Key -ne 'missing-library') {
        $source = Join-Path $root ('tests/NetWiv.Desktop.Smoke/abi-fixtures/' + $case.Key + '.rs')
        & rustc +1.95.0 --crate-type cdylib --edition 2021 $source -o (Join-Path $directory 'netwiv_native.dll')
        if ($LASTEXITCODE -ne 0) { throw "Failed to compile ABI fixture $($case.Key)" }
    }
    & $candidate --expect-startup-error $case.Value
    if ($LASTEXITCODE -ne 0) { throw "Startup diagnostic fixture failed: $($case.Key)" }
}
Write-Output "Startup fixtures preserved in $output"
