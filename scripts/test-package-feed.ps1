#requires -Version 7.0
param(
    [Parameter(Mandatory)][string]$Feed,
    [Parameter(Mandatory)][string]$Version,
    [ValidateSet("net8.0", "net9.0", "net10.0")][string]$TargetFramework = "net8.0"
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$Feed = [IO.Path]::GetFullPath($Feed)
$workspace = Join-Path ([IO.Path]::GetTempPath()) ('netwiv-package-check-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $workspace | Out-Null
$previousPackages = $env:NUGET_PACKAGES
try {
    # A fresh consumer outside the checkout cannot inherit source project targets or cached NetWiv packages.
    $env:NUGET_PACKAGES = Join-Path $workspace 'cache'
    Copy-Item -LiteralPath (Join-Path $repository 'global.json') -Destination $workspace
    $configuration = [xml]'<configuration><packageSources><clear/><add key="netwiv" value=""/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources><packageSourceMapping><clear/><packageSource key="netwiv"><package pattern="NetWiv.*"/></packageSource><packageSource key="nuget.org"><package pattern="*"/></packageSource></packageSourceMapping></configuration>'
    $configuration.configuration.packageSources.add[0].SetAttribute('value', $Feed)
    $configuration.Save((Join-Path $workspace 'NuGet.Config'))
    Push-Location $workspace
    try {
        & dotnet tool install NetWiv.Cli --version $Version --tool-path tools --configfile NuGet.Config
        if ($LASTEXITCODE -ne 0) { throw 'CLI package installation failed.' }
        $cli = Join-Path $workspace ('tools/netwiv' + $(if ($IsWindows) { '.exe' } else { '' }))
        $reportedVersion = & $cli --version
        if ($LASTEXITCODE -ne 0 -or $reportedVersion -ne "netwiv-cli $Version") { throw "Incorrect CLI version: $reportedVersion" }
        & $cli new PackageExample --template vanilla --source $Feed
        if ($LASTEXITCODE -ne 0) { throw 'CLI template creation from the installed package failed.' }
        & dotnet build PackageExample/PackageExample.sln -c Debug -p:NetWivDevelopment=true
        if ($LASTEXITCODE -ne 0) { throw 'Generated solution or packaged MSBuild tasks failed.' }
        if (!(Get-ChildItem PackageExample/src/generated -File)) { throw 'Packaged frontend generator produced no bindings.' }

        # Reuse the real desktop smoke source, replacing every project reference with the packaged dependency.
        $source = Join-Path $repository 'tests/NetWiv.Desktop.Smoke'
        $smoke = Join-Path $workspace 'smoke'
        New-Item -ItemType Directory -Path $smoke | Out-Null
        Get-ChildItem -LiteralPath $source | Where-Object { $_.Name -notin @('bin', 'obj') } |
            Copy-Item -Destination $smoke -Recurse
        $projectPath = Join-Path $smoke 'NetWiv.Desktop.Smoke.csproj'
        $project = [xml](Get-Content -LiteralPath $projectPath -Raw)
        $properties = $project.Project.PropertyGroup
        foreach ($setting in @{ TargetFramework=$TargetFramework; ImplicitUsings='enable'; Nullable='enable'; TreatWarningsAsErrors='true' }.GetEnumerator()) {
            $element = $project.CreateElement($setting.Key)
            $element.InnerText = $setting.Value
            [void]$properties.AppendChild($element)
        }
        foreach ($reference in @($project.SelectNodes('//ProjectReference'))) {
            $package = $project.CreateElement('PackageReference')
            $package.SetAttribute('Include', [IO.Path]::GetFileNameWithoutExtension($reference.Include))
            $package.SetAttribute('Version', $Version)
            [void]$reference.ParentNode.ReplaceChild($package, $reference)
        }
        $additionalPackages = $project.CreateElement('ItemGroup')
        foreach ($name in @('NetWiv.Hosting', 'NetWiv.Build', 'NetWiv.Generators')) {
            $package = $project.CreateElement('PackageReference')
            $package.SetAttribute('Include', $name)
            $package.SetAttribute('Version', $Version)
            [void]$additionalPackages.AppendChild($package)
        }
        [void]$project.Project.AppendChild($additionalPackages)
        foreach ($resource in $project.SelectNodes('//EmbeddedResource')) {
            if ($resource.Include.StartsWith('../')) {
                $resource.SetAttribute('Include', [IO.Path]::GetFullPath($resource.Include, $source))
            }
        }
        $project.Save($projectPath)
        @'
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetWiv.Hosting;

internal static class PackageBindingProbe
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Verify()
    {
        var builder = NetWivApplication.CreateBuilder(new Microsoft.Extensions.Hosting.HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Probe:Value"] = "bound" });
        builder.Services.Configure<PackageOptions>(builder.Configuration.GetSection("Probe"));
        using var provider = builder.Services.BuildServiceProvider();
        if (provider.GetRequiredService<IOptions<PackageOptions>>().Value.Value != "bound")
            throw new Exception("Packaged NativeAOT configuration binding failed.");
    }
}
internal sealed class PackageOptions { public string Value { get; set; } = ""; }
'@ | Set-Content -LiteralPath (Join-Path $smoke 'PackageBindingProbe.cs')
        & dotnet publish $projectPath -c Release -p:PublishAot=true -o published
        if ($LASTEXITCODE -ne 0) { throw 'Package-only NativeAOT publish failed.' }
        $executable = Join-Path $workspace ('published/NetWiv.Desktop.Smoke' + $(if ($IsWindows) { '.exe' } else { '' }))
        if ($IsLinux) {
            & xvfb-run -a bash (Join-Path $repository 'scripts/run-linux-smoke-desktop.sh') $executable x11 --no-window-smoke
        }
        else { & $executable --no-window-smoke }
        if ($LASTEXITCODE -ne 0) { throw 'Package-only native startup failed.' }
    }
    finally { Pop-Location }
    Write-Output "PASS ${TargetFramework}: packaged CLI, generated solution, configuration binding and package-only NativeAOT startup"
}
finally {
    $env:NUGET_PACKAGES = $previousPackages
    Remove-Item -LiteralPath $workspace -Recurse -Force
}
