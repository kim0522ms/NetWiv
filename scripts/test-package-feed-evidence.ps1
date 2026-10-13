#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('netwiv-package-evidence-' + [guid]::NewGuid().ToString('N'))
$savedEnvironment = @{}
foreach ($name in @('DOTNET_ROOT', 'DOTNET_ROOT_X64', 'DOTNET_ROOT_ARM64', 'DOTNET_ROOT_X86', 'DOTNET_ROOT(x86)',
    'DOTNET_ROLL_FORWARD', 'DOTNET_ROLL_FORWARD_ON_NO_CANDIDATE_FX', 'NUGET_PACKAGES')) {
    $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
    [Environment]::SetEnvironmentVariable($name, $null)
}
try {
    $feed = Join-Path $testRoot 'feed/packages'
    New-Item -ItemType Directory -Path $feed -Force | Out-Null
    $package = Join-Path $feed 'NetWiv.Core.0.1.0-test.nupkg'
    Set-Content -LiteralPath $package -Value 'synthetic package bytes; never restored'
    Set-Content -LiteralPath (Join-Path (Split-Path $feed) 'SOURCE.txt') -Value ('Source commit: ' + ('a' * 40))
    # Exercise a transferred source archive, which has no Git metadata.
    $archive = Join-Path $testRoot 'archive'
    New-Item -ItemType Directory -Path (Join-Path $archive 'scripts') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'test-package-feed.ps1') -Destination (Join-Path $archive 'scripts')
    Copy-Item -LiteralPath (Join-Path (Split-Path $PSScriptRoot) 'global.json') -Destination $archive
    $env:DOTNET_ROOT = Join-Path $testRoot 'dotnet'
    $env:NUGET_PACKAGES = Join-Path $testRoot 'original-cache'
    $global:netwivPackageEvidenceInstallAttempts = 0
    $global:netwivPackageEvidenceWorkspace = $null
    # Fail at the first restore. No network, GUI or real dotnet invocation occurs.
    function dotnet {
        $global:LASTEXITCODE = 0
        switch ($args[0]) {
            '--version' { '8.0.406' }
            '--list-runtimes' { "Microsoft.NETCore.App 8.0.13 [$env:DOTNET_ROOT/shared/Microsoft.NETCore.App]" }
            '--info' { 'synthetic isolated SDK information' }
            'tool' {
                $global:netwivPackageEvidenceInstallAttempts++
                $global:netwivPackageEvidenceWorkspace = (Get-Location).Path
                $global:LASTEXITCODE = 1
            }
            default { throw "Unexpected dotnet invocation: $args" }
        }
    }
    function Get-Command {
        param([string]$Name)
        if ($Name -ne 'dotnet') { throw "Unexpected command lookup: $Name" }
        @{ Source = (Join-Path $env:DOTNET_ROOT 'dotnet') }
    }
    $evidence = Join-Path $testRoot 'evidence'
    $initialLocation = (Get-Location).Path
    try {
        & (Join-Path $archive 'scripts/test-package-feed.ps1') -Feed $feed -Version '0.1.0-test' -EvidenceDirectory $evidence
        throw 'Expected consumer restore failure.'
    }
    catch {
        if ($_.Exception.Message -ne 'CLI package installation failed.') { throw }
    }
    if ($global:netwivPackageEvidenceInstallAttempts -ne 1) { throw 'Did not reach the isolated package restore.' }
    if ((Get-Location).Path -ne $initialLocation) { throw 'Consumer failure changed the working directory.' }
    if ($env:NUGET_PACKAGES -ne (Join-Path $testRoot 'original-cache')) { throw 'Consumer failure changed the package cache environment.' }
    if (Test-Path -LiteralPath $global:netwivPackageEvidenceWorkspace) { throw 'Failed consumer workspace was not removed.' }
    $provenance = Get-Content -LiteralPath (Join-Path $evidence 'provenance.json') -Raw | ConvertFrom-Json
    if ($provenance.version -ne '0.1.0-test' -or $provenance.revision -ne ('a' * 40) -or $provenance.packages.Count -ne 1 -or
        $provenance.packages[0].Hash -ne (Get-FileHash -LiteralPath $package).Hash) {
        throw 'Failure evidence does not identify the exact tested package bytes.'
    }
    foreach ($file in @('package-consumer.log', 'dotnet-info.txt', 'SOURCE.txt')) {
        if (!(Test-Path -LiteralPath (Join-Path $evidence $file))) { throw "Missing failure evidence: $file" }
    }
    if ((Get-Content -LiteralPath (Join-Path $evidence 'package-consumer.log') -Raw) -notmatch 'CLI package installation failed\.') {
        throw 'The failure reason was not retained in the transcript.'
    }
    Write-Output 'PASS package consumer failure: retained archive revision, package provenance and diagnostics, restored environment, removed isolated workspace'
}
finally {
    Remove-Variable -Name netwivPackageEvidenceInstallAttempts, netwivPackageEvidenceWorkspace -Scope Global -ErrorAction SilentlyContinue
    foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name]) }
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}
