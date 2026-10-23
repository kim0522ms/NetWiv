#requires -Version 7.0
param(
    [Parameter(Mandatory)][string]$Destination,
    [Parameter(Mandatory)][string]$Version,
    [ValidateSet('net8.0', 'net9.0', 'net10.0')][string]$TargetFramework = 'net8.0'
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$Destination = [IO.Path]::GetFullPath($Destination)

# Test-only plugin and Razor library references remain separate consumer projects.
# Only framework references become packages; the fixtures are never published packages.
foreach ($name in @('NetWiv.Plugin.CommandFixture', 'NetWiv.Desktop.Smoke', 'NetWiv.BlazorHybrid.Managed.Fixture', 'NetWiv.BlazorHybrid.Aot.Tests')) {
    $source = Join-Path $repository "tests/$name"
    $directory = Join-Path $Destination $name
    New-Item -ItemType Directory -Path $directory | Out-Null
    Get-ChildItem -LiteralPath $source | Where-Object { $_.Name -notin @('bin', 'obj') } |
        Copy-Item -Destination $directory -Recurse
    $projectPath = Join-Path $directory "$name.csproj"
    $project = [xml](Get-Content -LiteralPath $projectPath -Raw)
    $properties = $project.Project.PropertyGroup
    foreach ($setting in @{ TargetFramework=$TargetFramework; ImplicitUsings='enable'; Nullable='enable'; TreatWarningsAsErrors='true' }.GetEnumerator()) {
        $element = $project.CreateElement($setting.Key)
        $element.InnerText = $setting.Value
        [void]$properties.PrependChild($element)
    }
    foreach ($reference in @($project.SelectNodes('//ProjectReference'))) {
        $id = [IO.Path]::GetFileNameWithoutExtension($reference.Include)
        if ($id -in @('NetWiv.Plugin.CommandFixture', 'NetWiv.BlazorHybrid.Managed.Fixture')) {
            $reference.SetAttribute('Include', "../$id/$id.csproj")
            continue
        }
        $package = $project.CreateElement('PackageReference')
        $package.SetAttribute('Include', $id)
        $package.SetAttribute('Version', $Version)
        [void]$reference.ParentNode.ReplaceChild($package, $reference)
    }
    if ($name -eq 'NetWiv.Desktop.Smoke') {
        $additionalPackages = $project.CreateElement('ItemGroup')
        foreach ($id in @('NetWiv.Hosting', 'NetWiv.Build', 'NetWiv.Generators')) {
            if ($project.SelectSingleNode("//PackageReference[@Include='$id']")) { continue }
            $package = $project.CreateElement('PackageReference')
            $package.SetAttribute('Include', $id)
            $package.SetAttribute('Version', $Version)
            [void]$additionalPackages.AppendChild($package)
        }
        [void]$project.Project.AppendChild($additionalPackages)
    }
    if ($name -eq 'NetWiv.BlazorHybrid.Aot.Tests') {
        # Keep the upstream fixture notice portable outside the source checkout.
        $notice = $project.SelectSingleNode("//None[@Include='../../licenses/dotnet-runtime-MIT.txt']")
        $notice.SetAttribute('Include', 'native-tests-MIT.txt')
        Copy-Item -LiteralPath (Join-Path $repository 'licenses/dotnet-runtime-MIT.txt') -Destination (Join-Path $directory 'native-tests-MIT.txt')
    }
    # Evaluate MSBuild globs and metadata in the original project before moving
    # resources. Copy each resolved file and preserve its exact manifest name.
    $evaluated = & dotnet msbuild (Join-Path $source "$name.csproj") -getItem:EmbeddedResource -nologo
    if ($LASTEXITCODE -ne 0) { throw 'Could not evaluate smoke resources.' }
    $externalResources = @(($evaluated | ConvertFrom-Json).Items.EmbeddedResource |
        Where-Object { $_.Identity.Replace('\', '/').StartsWith('../') })
    $resourceGroup = $project.CreateElement('ItemGroup')
    foreach ($resource in @($project.SelectNodes('//EmbeddedResource'))) {
        if ($resource.Include.StartsWith('../')) { [void]$resource.ParentNode.RemoveChild($resource) }
    }
    foreach ($resource in $externalResources) {
        $relative = Join-Path 'package-fixtures' ([IO.Path]::GetRelativePath($repository, $resource.FullPath))
        $destinationPath = Join-Path $directory $relative
        New-Item -ItemType Directory -Path (Split-Path $destinationPath) -Force | Out-Null
        Copy-Item -LiteralPath $resource.FullPath -Destination $destinationPath
        $element = $project.CreateElement('EmbeddedResource')
        $element.SetAttribute('Include', $relative)
        $element.SetAttribute('LogicalName', $resource.LogicalName)
        [void]$resourceGroup.AppendChild($element)
    }
    [void]$project.Project.AppendChild($resourceGroup)
    $project.Save($projectPath)
}
