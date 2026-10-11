#requires -Version 7.0
param(
    [Parameter(Mandatory)][string]$Feed,
    [Parameter(Mandatory)][string]$Version,
    [ValidateSet("net8.0", "net9.0", "net10.0")][string]$TargetFramework = "net8.0"
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$Feed = [IO.Path]::GetFullPath($Feed)
# SDK selection alone does not isolate the runtimes used by net8.0 apphosts.
$major = $TargetFramework.Substring(3).Split('.')[0]
$sdk = & dotnet --version
if ($LASTEXITCODE -ne 0 -or $sdk -notlike "$major.*") { throw "Expected SDK major $major, found $sdk." }
$runtimes = @(& dotnet --list-runtimes)
if ($LASTEXITCODE -ne 0 -or !($runtimes -match "^Microsoft\.NETCore\.App $major\.") -or
    @($runtimes | Where-Object { $_ -notmatch "^\S+ $major\." }).Count) {
    throw "Expected only runtime major $major in the selected dotnet installation: $($runtimes -join '; ')"
}
$dotnetRoot = Split-Path (Get-Command dotnet).Source -Parent
if (!$env:DOTNET_ROOT -or [IO.Path]::GetFullPath($env:DOTNET_ROOT) -ne $dotnetRoot) {
    throw 'DOTNET_ROOT must point to the selected private dotnet installation.'
}
foreach ($name in 'DOTNET_ROOT_X64', 'DOTNET_ROOT_ARM64', 'DOTNET_ROOT_X86', 'DOTNET_ROOT(x86)') {
    $value = [Environment]::GetEnvironmentVariable($name)
    if ($value -and [IO.Path]::GetFullPath($value) -ne $dotnetRoot) { throw "$name points outside the isolated dotnet installation." }
}
foreach ($name in 'DOTNET_ROLL_FORWARD', 'DOTNET_ROLL_FORWARD_ON_NO_CANDIDATE_FX') {
    if ([Environment]::GetEnvironmentVariable($name)) { throw "$name would override the application's runtime policy." }
}
Write-Output "Validating SDK $sdk with only runtime major $major"
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
        & $cli new PackageExample --template react --source $Feed
        if ($LASTEXITCODE -ne 0) { throw 'CLI template creation from the installed package failed.' }
        @'
internal static class ManagedRuntimeProbe
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Verify()
    {
        if (Environment.Version.Major != __MAJOR__)
            throw new Exception("Generated app used an unexpected runtime: " + Environment.Version);
        Console.WriteLine("PASS managed runtime " + Environment.Version.Major);
    }
}
'@.Replace('__MAJOR__', $major) | Set-Content PackageExample/src-netwiv/ManagedRuntimeProbe.cs
        & dotnet build PackageExample/PackageExample.sln -c Debug -warnaserror
        if ($LASTEXITCODE -ne 0) { throw 'Generated solution or packaged MSBuild tasks failed.' }
        if (!(Get-ChildItem PackageExample/src/generated -File)) { throw 'Packaged frontend generator produced no bindings.' }
        $managed = Join-Path $workspace ('PackageExample/src-netwiv/bin/Debug/net8.0/PackageExample' + $(if ($IsWindows) { '.exe' } else { '' }))
        if ($IsLinux) {
            $managedOutput = @(& xvfb-run -a bash (Join-Path $repository 'scripts/run-linux-smoke-desktop.sh') $managed x11 --smoke-test)
        }
        else { $managedOutput = @(& $managed --smoke-test) }
        $managedExit = $LASTEXITCODE
        $managedOutput | Write-Output
        if ($managedExit -ne 0 -or !($managedOutput -contains "PASS managed runtime $major") -or
            !($managedOutput -contains 'PASS: React form → generated C# → JSON')) {
            throw 'Generated managed application smoke failed.'
        }

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
    Write-Output "PASS ${TargetFramework}: packaged CLI, generated managed GUI/IPC, configuration binding and package-only NativeAOT startup"
}
finally {
    $env:NUGET_PACKAGES = $previousPackages
    Remove-Item -LiteralPath $workspace -Recurse -Force
}
