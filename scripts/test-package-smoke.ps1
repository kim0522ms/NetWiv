#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('netwiv-package-project-' + [guid]::NewGuid().ToString('N'))
try {
    foreach ($framework in @('net8.0', 'net9.0', 'net10.0')) {
        $destination = Join-Path $root $framework
        & "$PSScriptRoot/prepare-package-smoke.ps1" -Destination $destination -Version '0.1.0-test' -TargetFramework $framework
        foreach ($name in @('NetWiv.Desktop.Smoke', 'NetWiv.Plugin.CommandFixture', 'NetWiv.BlazorHybrid.Aot.Tests')) {
            $path = Join-Path $destination "$name/$name.csproj"
            $project = [xml](Get-Content $path -Raw)
            if ($project.Project.PropertyGroup.TargetFramework -ne $framework) { throw 'Incorrect target framework.' }
            $packages = @($project.SelectNodes('//PackageReference'))
            if (@($packages | Group-Object Include | Where-Object Count -gt 1).Count) { throw 'Duplicate package references.' }
            if (@($packages | Where-Object { $_.Include -like 'NetWiv.*' -and $_.Version -ne '0.1.0-test' }).Count) { throw 'Incorrect package version.' }
            if ($name -ne 'NetWiv.BlazorHybrid.Aot.Tests' -and @($packages | Where-Object Include -eq 'NetWiv.Generators').Count -ne 1) { throw 'Expected one analyzer package per project.' }
            if (@($packages | Where-Object Include -eq 'NetWiv.Plugin.CommandFixture').Count) { throw 'The fixture is not a published package.' }
            foreach ($reference in $project.SelectNodes('//ProjectReference')) {
                if ($reference.Include -ne '../NetWiv.Plugin.CommandFixture/NetWiv.Plugin.CommandFixture.csproj') {
                    throw "Framework source reference escaped conversion: $($reference.Include)"
                }
                if (!(Test-Path ([IO.Path]::GetFullPath($reference.Include, (Split-Path $path))))) { throw 'Fixture project missing.' }
            }
        }
        $hybridDirectory = Join-Path $destination 'NetWiv.BlazorHybrid.Aot.Tests'
        $hybrid = [xml](Get-Content (Join-Path $hybridDirectory 'NetWiv.BlazorHybrid.Aot.Tests.csproj') -Raw)
        if (@($hybrid.SelectNodes('//ProjectReference')).Count) { throw 'Hybrid consumer still references framework sources.' }
        foreach ($id in @('NetWiv.BlazorHybrid', 'NetWiv.Platform.Desktop')) {
            if (!$hybrid.SelectSingleNode("//PackageReference[@Include='$id']")) { throw "Missing $id package probe." }
        }
        if (!(Test-Path (Join-Path $hybridDirectory 'native-tests-MIT.txt')) -or
            !(Test-Path (Join-Path $hybridDirectory 'wwwroot/index.html'))) { throw 'Hybrid consumer notice or browser fixture missing.' }
        $smoke = [xml](Get-Content (Join-Path $destination 'NetWiv.Desktop.Smoke/NetWiv.Desktop.Smoke.csproj') -Raw)
        if (@($smoke.SelectNodes('//ProjectReference')).Count -ne 1) { throw 'The external-assembly regression fixture was lost.' }
        foreach ($id in @('NetWiv.Hosting', 'NetWiv.Build')) {
            if (!$smoke.SelectSingleNode("//PackageReference[@Include='$id']")) { throw "Missing $id package probe." }
        }
        $resources = @($smoke.SelectNodes('//EmbeddedResource') | Where-Object Include -like 'package-fixtures*')
        $originalItems = & dotnet msbuild tests/NetWiv.Desktop.Smoke -getItem:EmbeddedResource -nologo
        if ($LASTEXITCODE -ne 0) { throw 'Could not evaluate original smoke resources.' }
        $expected = @(($originalItems | ConvertFrom-Json).Items.EmbeddedResource | Where-Object { $_.Identity.Replace('\', '/').StartsWith('../') })
        if ($resources.Count -ne $expected.Count -or $resources.Count -eq 0) { throw 'External smoke resources were lost.' }
        $expectedNames = @($expected | ForEach-Object LogicalName | Sort-Object)
        $actualNames = @($resources | ForEach-Object LogicalName | Sort-Object)
        if (Compare-Object $expectedNames $actualNames) { throw 'Consumer manifest resource names changed.' }
        foreach ($name in @('netwiv.assets.core.js', 'netwiv.assets.plugins/global-shortcut.js', 'netwiv.assets.upstream/app.js', 'netwiv.assets.upstream/image.js')) {
            if ($name -notin @($actualNames | ForEach-Object { $_.Replace('\', '/') })) { throw "Required frontend resource missing: $name" }
        }
        $repository = Split-Path $PSScriptRoot -Parent
        $consumer = Join-Path $destination 'NetWiv.Desktop.Smoke'
        foreach ($resource in $resources) {
            if ([IO.Path]::IsPathRooted($resource.Include)) { throw 'Consumer resource still depends on an absolute checkout path.' }
            $original = Join-Path $repository $resource.Include.Substring('package-fixtures/'.Length)
            $copied = Join-Path $consumer $resource.Include
            if ((Get-FileHash -LiteralPath $original).Hash -ne (Get-FileHash -LiteralPath $copied).Hash) {
                throw "Copied resource differs from its fixture: $($resource.Include)"
            }
        }
        if (!(Test-Path (Join-Path $destination 'NetWiv.Plugin.CommandFixture/ExternalCommandPlugin.cs'))) { throw 'Fixture source missing.' }
        Write-Output "PASS $framework package consumer project: unique dependencies, separate plugin assembly, no framework source references"
    }
}
finally { if (Test-Path $root) { Remove-Item $root -Recurse -Force } }
