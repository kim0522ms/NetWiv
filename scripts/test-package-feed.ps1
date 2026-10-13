#requires -Version 7.0
param(
    [Parameter(Mandatory)][string]$Feed,
    [Parameter(Mandatory)][string]$Version,
    [ValidateSet("net8.0", "net9.0", "net10.0")][string]$TargetFramework = "net8.0",
    [string]$EvidenceDirectory
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
$transcribing = $false
try {
    if ($EvidenceDirectory) {
        $EvidenceDirectory = [IO.Path]::GetFullPath($EvidenceDirectory)
        New-Item -ItemType Directory -Path $EvidenceDirectory -Force | Out-Null
        Start-Transcript -LiteralPath (Join-Path $EvidenceDirectory 'package-consumer.log') | Out-Null
        $transcribing = $true
        & dotnet --info | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'dotnet-info.txt')
        @{
            version = $Version
            targetFramework = $TargetFramework
            revision = (& git -C $repository rev-parse HEAD)
            os = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
            architecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
            packages = @(Get-ChildItem -LiteralPath $Feed -Filter '*.nupkg' | Get-FileHash -Algorithm SHA256 |
                Select-Object @{Name='file';Expression={Split-Path $_.Path -Leaf}}, Hash)
        } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'provenance.json')
        $sourceRecord = Join-Path (Split-Path $Feed) 'SOURCE.txt'
        if (Test-Path -LiteralPath $sourceRecord) {
            Copy-Item -LiteralPath $sourceRecord -Destination (Join-Path $EvidenceDirectory 'SOURCE.txt')
        }
    }
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
        Push-Location PackageExample
        try { & $cli build --framework-dependent --no-bundle -- -warnaserror }
        finally { Pop-Location }
        if ($LASTEXITCODE -ne 0) { throw 'Generated solution or packaged MSBuild tasks failed.' }
        if (!(Get-ChildItem PackageExample/src/generated -File)) { throw 'Packaged frontend generator produced no bindings.' }
        $managedFiles = @(Get-ChildItem PackageExample/target -Recurse -File |
            Where-Object { $_.Name -eq ('PackageExample' + $(if ($IsWindows) { '.exe' } else { '' })) -and $_.Directory.Name -eq 'release' })
        if ($managedFiles.Count -ne 1) { throw 'Expected one published managed apphost.' }
        $managed = $managedFiles[0].FullName
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

        if ($IsMacOS) {
            Push-Location PackageExample
            try { & $cli bundle --bundles app --no-sign }
            finally { Pop-Location }
            if ($LASTEXITCODE -ne 0) { throw 'Generated managed application bundle failed.' }
            $app = Join-Path (Split-Path $managed) 'bundle/macos/PackageExample.app/Contents/MacOS/PackageExample'
            $bundledOutput = @(& $app --smoke-test)
            $bundledExit = $LASTEXITCODE
            $bundledOutput | Write-Output
            if ($bundledExit -ne 0 -or !($bundledOutput -contains "PASS managed runtime $major") -or
                !($bundledOutput -contains 'PASS: React form → generated C# → JSON')) {
                throw 'Bundled managed application smoke failed.'
            }
        }

        # Generate package consumers, retaining the test plugin as a separate assembly.
        & "$PSScriptRoot/prepare-package-smoke.ps1" -Destination $workspace -Version $Version -TargetFramework $TargetFramework
        $smoke = Join-Path $workspace 'NetWiv.Desktop.Smoke'
        $projectPath = Join-Path $smoke 'NetWiv.Desktop.Smoke.csproj'
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
catch {
    Write-Output "FAIL package consumer: $($_.Exception.Message)"
    throw
}
finally {
    if ($transcribing) { Stop-Transcript | Out-Null }
    $env:NUGET_PACKAGES = $previousPackages
    Remove-Item -LiteralPath $workspace -Recurse -Force
}
