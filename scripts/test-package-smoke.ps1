#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('netwiv-package-project-' + [guid]::NewGuid().ToString('N'))
try {
    foreach ($framework in @('net8.0', 'net9.0', 'net10.0')) {
        $destination = Join-Path $root $framework
        & "$PSScriptRoot/prepare-package-smoke.ps1" -Destination $destination -Version '0.1.0-test' -TargetFramework $framework
        foreach ($name in @('NetWiv.Desktop.Smoke', 'NetWiv.Plugin.CommandFixture')) {
            $path = Join-Path $destination "$name/$name.csproj"
            $project = [xml](Get-Content $path -Raw)
            if ($project.Project.PropertyGroup.TargetFramework -ne $framework) { throw 'Incorrect target framework.' }
            $packages = @($project.SelectNodes('//PackageReference'))
            if (@($packages | Group-Object Include | Where-Object Count -gt 1).Count) { throw 'Duplicate package references.' }
            if (@($packages | Where-Object Version -ne '0.1.0-test').Count) { throw 'Incorrect package version.' }
            if (@($packages | Where-Object Include -eq 'NetWiv.Generators').Count -ne 1) { throw 'Expected one analyzer package per project.' }
            if (@($packages | Where-Object Include -eq 'NetWiv.Plugin.CommandFixture').Count) { throw 'The fixture is not a published package.' }
            foreach ($reference in $project.SelectNodes('//ProjectReference')) {
                if ($reference.Include -ne '../NetWiv.Plugin.CommandFixture/NetWiv.Plugin.CommandFixture.csproj') {
                    throw "Framework source reference escaped conversion: $($reference.Include)"
                }
                if (!(Test-Path ([IO.Path]::GetFullPath($reference.Include, (Split-Path $path))))) { throw 'Fixture project missing.' }
            }
        }
        $smoke = [xml](Get-Content (Join-Path $destination 'NetWiv.Desktop.Smoke/NetWiv.Desktop.Smoke.csproj') -Raw)
        if (@($smoke.SelectNodes('//ProjectReference')).Count -ne 1) { throw 'The external-assembly regression fixture was lost.' }
        foreach ($id in @('NetWiv.Hosting', 'NetWiv.Build')) {
            if (!$smoke.SelectSingleNode("//PackageReference[@Include='$id']")) { throw "Missing $id package probe." }
        }
        $resources = @($smoke.SelectNodes('//EmbeddedResource') | Where-Object Include -like 'package-fixtures*')
        if ($resources.Count -ne 3) { throw 'Expected all external smoke resources to be copied.' }
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
