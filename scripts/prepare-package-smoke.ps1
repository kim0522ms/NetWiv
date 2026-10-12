#requires -Version 7.0
param(
    [Parameter(Mandatory)][string]$Destination,
    [Parameter(Mandatory)][string]$Version,
    [ValidateSet('net8.0', 'net9.0', 'net10.0')][string]$TargetFramework = 'net8.0'
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$Destination = [IO.Path]::GetFullPath($Destination)

# The external plugin remains a separate consumer assembly. Only framework
# references become packages; the test-only fixture is never a published package.
foreach ($name in @('NetWiv.Plugin.CommandFixture', 'NetWiv.Desktop.Smoke')) {
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
        [void]$properties.AppendChild($element)
    }
    foreach ($reference in @($project.SelectNodes('//ProjectReference'))) {
        $id = [IO.Path]::GetFileNameWithoutExtension($reference.Include)
        if ($id -eq 'NetWiv.Plugin.CommandFixture') {
            $reference.SetAttribute('Include', '../NetWiv.Plugin.CommandFixture/NetWiv.Plugin.CommandFixture.csproj')
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
    foreach ($resource in $project.SelectNodes('//EmbeddedResource')) {
        if ($resource.Include.StartsWith('../')) {
            $resource.SetAttribute('Include', [IO.Path]::GetFullPath($resource.Include, $source))
        }
    }
    $project.Save($projectPath)
}
