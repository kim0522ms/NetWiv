#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$privateMarker = 'SYNTHETIC-PRIVATE-IDENTIFIER'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('netwiv-package-evidence-' + $privateMarker + '-' + [guid]::NewGuid().ToString('N'))
$savedEnvironment = @{}
foreach ($name in @('DOTNET_ROOT', 'DOTNET_ROOT_X64', 'DOTNET_ROOT_ARM64', 'DOTNET_ROOT_X86', 'DOTNET_ROOT(x86)',
    'DOTNET_ROLL_FORWARD', 'DOTNET_ROLL_FORWARD_ON_NO_CANDIDATE_FX', 'NUGET_PACKAGES')) {
    $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
    [Environment]::SetEnvironmentVariable($name, $null)
}
$global:netwivPackageEvidenceFunctions = @()
try {
    $feed = Join-Path $testRoot 'feed/packages'
    New-Item -ItemType Directory -Path $feed -Force | Out-Null
    $version = '0.1.0-' + $privateMarker
    $package = Join-Path $feed "NetWiv.Core.$version.nupkg"
    Set-Content -LiteralPath $package -Value 'synthetic package bytes; never restored'
    Set-Content -LiteralPath (Join-Path (Split-Path $feed) 'SOURCE.txt') -Value @(
        ('Source commit: ' + ('a' * 40)), "Private source annotation: $privateMarker")
    # Exercise a transferred source archive, which has no Git metadata.
    $archive = Join-Path $testRoot 'archive'
    New-Item -ItemType Directory -Path (Join-Path $archive 'scripts') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'test-package-feed.ps1') -Destination (Join-Path $archive 'scripts')
    Copy-Item -LiteralPath (Join-Path (Split-Path $PSScriptRoot) 'global.json') -Destination $archive
    # The success fixture substitutes every external execution. No installed
    # CLI, dotnet, native application, restore, network or GUI is ever invoked.
    @'
param($Destination, $Version, $TargetFramework)
New-Item -ItemType Directory -Path (Join-Path $Destination 'NetWiv.Desktop.Smoke') -Force | Out-Null
Set-Content -LiteralPath (Join-Path $Destination 'NetWiv.Desktop.Smoke/NetWiv.Desktop.Smoke.csproj') -Value '<Project />'
'@ | Set-Content -LiteralPath (Join-Path $archive 'scripts/prepare-package-smoke.ps1')
    $env:DOTNET_ROOT = Join-Path $testRoot 'dotnet'
    $env:NUGET_PACKAGES = Join-Path $testRoot 'original-cache'
    $global:netwivPackageEvidenceVersion = $version
    $global:netwivPackageEvidenceMarker = $privateMarker
    function dotnet {
        $global:LASTEXITCODE = 0
        switch ($args[0]) {
            '--version' { '8.0.406' }
            '--list-runtimes' {
                $runtime = if ($global:netwivPackageEvidenceScenario -eq 'preflight') { '9.0.1' } else { '8.0.13' }
                "Microsoft.NETCore.App $runtime [$env:DOTNET_ROOT/shared/Microsoft.NETCore.App]"
            }
            'tool' {
                $global:netwivPackageEvidenceInstallAttempts++
                $global:netwivPackageEvidenceWorkspace = (Get-Location).Path
                Write-Output "synthetic command diagnostics: $global:netwivPackageEvidenceMarker"
                if ($global:netwivPackageEvidenceScenario -eq 'unexpected') {
                    throw "$global:netwivPackageEvidenceMarker unexpected restore exception"
                }
                if ($global:netwivPackageEvidenceScenario -eq 'restore') {
                    $global:LASTEXITCODE = 1
                    return
                }
                $cli = Join-Path $global:netwivPackageEvidenceWorkspace ('tools/netwiv' + $(if ($IsWindows) { '.exe' } else { '' }))
                $global:netwivPackageEvidenceFunctions += $cli
                Set-Item -LiteralPath "function:global:$cli" -Value {
                    $global:LASTEXITCODE = 0
                    switch ($args[0]) {
                        '--version' { "netwiv-cli $global:netwivPackageEvidenceVersion" }
                        'new' {
                            New-Item -ItemType Directory -Path 'PackageExample/src-netwiv' -Force | Out-Null
                        }
                        'build' {
                            New-Item -ItemType Directory -Path 'src/generated', 'target/release' -Force | Out-Null
                            Set-Content -LiteralPath 'src/generated/bindings.ts' -Value '// synthetic bindings'
                            $app = Join-Path (Get-Location).Path ('target/release/PackageExample' + $(if ($IsWindows) { '.exe' } else { '' }))
                            Set-Content -LiteralPath $app -Value 'synthetic executable; never launched'
                            $runtimeOptions = @{}
                            if ($args -notcontains '--framework-dependent') { $runtimeOptions.includedFrameworks = @(@{ name = 'Microsoft.NETCore.App'; version = '8.0.13' }) }
                            @{ runtimeOptions = $runtimeOptions } | ConvertTo-Json -Depth 4 | Set-Content 'target/release/PackageExample.runtimeconfig.json'
                            $global:netwivPackageEvidenceFunctions += $app
                            Set-Item -LiteralPath "function:global:$app" -Value {
                                $global:LASTEXITCODE = 0
                                "synthetic app diagnostics: $global:netwivPackageEvidenceMarker"
                                'PASS managed runtime 8'
                                if ($global:netwivPackageEvidenceScenario -ne 'smoke') { 'PASS: React form → generated C# → JSON' }
                            }
                        }
                        'bundle' {
                            $app = Join-Path (Get-Location).Path 'target/release/bundle/macos/PackageExample.app/Contents/MacOS/PackageExample'
                            $global:netwivPackageEvidenceFunctions += $app
                            Set-Item -LiteralPath "function:global:$app" -Value {
                                $global:LASTEXITCODE = 0
                                'PASS managed runtime 8'
                                'PASS: React form → generated C# → JSON'
                            }
                        }
                        default { throw 'Unexpected synthetic CLI invocation.' }
                    }
                }
            }
            'publish' {
                $app = Join-Path $global:netwivPackageEvidenceWorkspace ('published/NetWiv.Desktop.Smoke' + $(if ($IsWindows) { '.exe' } else { '' }))
                $global:netwivPackageEvidenceFunctions += $app
                Set-Item -LiteralPath "function:global:$app" -Value { $global:LASTEXITCODE = 0 }
            }
            default { throw 'Unexpected dotnet invocation; raw SDK diagnostics are forbidden in evidence.' }
        }
    }
    function Get-Command {
        param([string]$Name)
        if ($Name -ne 'dotnet') { throw "Unexpected command lookup: $Name" }
        @{ Source = (Join-Path $env:DOTNET_ROOT 'dotnet') }
    }
    function xvfb-run { & $args[3] $args[5] }
    $evidence = Join-Path $testRoot 'evidence'
    New-Item -ItemType Directory -Path $evidence -Force | Out-Null
    # A reused directory may already contain private evidence from older runs.
    # Preserve it; the workflow must upload only the new explicit public file.
    $staleFiles = @('package-consumer.log', 'dotnet-info.txt', 'SOURCE.txt', 'provenance.json', 'unrelated-user-file.txt')
    foreach ($file in $staleFiles) { Set-Content -LiteralPath (Join-Path $evidence $file) -Value $privateMarker }
    $initialLocation = (Get-Location).Path
    foreach ($scenario in 'restore', 'unexpected', 'preflight', 'smoke', 'success') {
        $global:netwivPackageEvidenceScenario = $scenario
        $global:netwivPackageEvidenceInstallAttempts = 0
        $global:netwivPackageEvidenceWorkspace = $null
        # Overwrite an old successful safe report even when preflight fails.
        Set-Content -LiteralPath (Join-Path $evidence 'public-package-consumer.json') -Value '{"status":"passed","stage":"complete"}'
        $caught = $null
        try {
            & (Join-Path $archive 'scripts/test-package-feed.ps1') -Feed $feed -Version $version -EvidenceDirectory $evidence
        }
        catch { $caught = $_ }
        switch ($scenario) {
            'restore' { if ($caught.Exception.Message -ne 'CLI package installation failed.') { throw 'Expected consumer restore failure was not propagated.' } }
            'unexpected' { if ($caught.Exception.Message -ne "$privateMarker unexpected restore exception") { throw 'Unexpected identifying exception was not propagated.' } }
            'preflight' { if ($caught.Exception.Message -notlike 'Expected only runtime major 8*') { throw 'Expected preflight failure was not propagated.' } }
            'smoke' { if ($caught.Exception.Message -ne 'Generated framework-dependent managed application smoke failed.') { throw 'Missing IPC output was not rejected.' } }
            'success' { if ($caught) { throw $caught } }
        }
        $attempts = if ($scenario -eq 'preflight') { 0 } else { 1 }
        if ($global:netwivPackageEvidenceInstallAttempts -ne $attempts) { throw 'Did not reach the expected isolated package restore.' }
        if ((Get-Location).Path -ne $initialLocation) { throw 'Consumer changed the working directory.' }
        if ($env:NUGET_PACKAGES -ne (Join-Path $testRoot 'original-cache')) { throw 'Consumer changed the package cache environment.' }
        if ($global:netwivPackageEvidenceWorkspace -and (Test-Path -LiteralPath $global:netwivPackageEvidenceWorkspace)) { throw 'Consumer workspace was not removed.' }
        foreach ($file in $staleFiles) {
            if ((Get-Content -LiteralPath (Join-Path $evidence $file) -Raw).Trim() -ne $privateMarker) { throw 'Reused evidence directory user files were changed.' }
        }
        $json = Get-Content -LiteralPath (Join-Path $evidence 'public-package-consumer.json') -Raw
        $public = $json | ConvertFrom-Json
        if ($json.Contains($privateMarker) -or $json.Contains($testRoot) -or $json.Contains('CLI package installation failed')) { throw 'Private diagnostics entered public evidence.' }
        if (($public.PSObject.Properties.Name | Sort-Object) -join ',' -ne 'packageHashes,revision,rid,sdkVersion,stage,status,targetFramework,version') { throw 'Public evidence contains unapproved fields.' }
        if ($public.version -ne '0.1.0' -or $public.sdkVersion -ne '8.0.406' -or $public.targetFramework -ne 'net8.0' -or
            $public.rid -notmatch '^(win|linux|osx)-(x64|arm64|x86)$') { throw 'Safe public version/RID evidence is missing.' }
        if ($scenario -eq 'preflight') {
            if ($public.stage -ne 'preflight' -or $public.revision -or $public.packageHashes.Count) { throw 'Preflight evidence claims inputs that were not tested.' }
        }
        elseif ($public.revision -ne ('a' * 40) -or $public.packageHashes.Count -ne 1 -or
            $public.packageHashes[0] -ne (Get-FileHash -LiteralPath $package).Hash) { throw 'Evidence does not identify the exact tested package bytes.' }
        $expectedStage = switch ($scenario) { 'restore' { 'cli-install' } 'unexpected' { 'cli-install' } 'preflight' { 'preflight' } 'smoke' { 'managed-smoke' } 'success' { 'complete' } }
        $expectedStatus = if ($scenario -eq 'success') { 'passed' } else { 'failed' }
        if ($public.stage -ne $expectedStage -or $public.status -ne $expectedStatus) { throw 'Evidence reports an incorrect consumer result.' }
        if (@(Get-ChildItem -LiteralPath $evidence -File).Count -ne $staleFiles.Count + 1) { throw 'Unexpected evidence file created.' }
    }
    Write-Output 'PASS package consumer failure: safe archive revision and package hashes, private exceptions excluded, restored environment, removed isolated workspace'
    Write-Output 'PASS package consumer success: mocked complete consumer path, allowlisted evidence, stale private files preserved'
}
finally {
    foreach ($name in $global:netwivPackageEvidenceFunctions) { Remove-Item -LiteralPath "function:global:$name" -ErrorAction SilentlyContinue }
    Remove-Variable -Name netwivPackageEvidenceFunctions, netwivPackageEvidenceVersion, netwivPackageEvidenceMarker,
        netwivPackageEvidenceScenario, netwivPackageEvidenceInstallAttempts, netwivPackageEvidenceWorkspace -Scope Global -ErrorAction SilentlyContinue
    foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name]) }
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}
# Clear mocked failures only after assertions and cleanup succeed. The Actions
# PowerShell wrapper forwards LASTEXITCODE even when the expected error was caught.
$global:LASTEXITCODE = 0
