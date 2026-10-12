# Prerequisite tests temporarily change WebView2 registration and restore it in finally.
# Local execution requires an explicit opt-in on a dedicated elevated validation machine.
param([switch]$AllowLocalMachine)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows -or ($env:CI -ne 'true' -and -not $AllowLocalMachine)) { throw 'Use a Windows CI runner or explicitly pass -AllowLocalMachine on a dedicated validation machine.' }
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Per-machine installer acceptance requires an elevated runner.'
}
$repo = Split-Path $PSScriptRoot -Parent
$root = Join-Path $repo 'artifacts/ci/windows-installers'
New-Item -ItemType Directory -Path $root -Force | Out-Null
$cli = Join-Path $repo 'src/NetWiv.Cli/bin/Release/net8.0/netwiv.dll'
$fixture = Join-Path $root 'fixture'
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><PublishAot>true</PublishAot></PropertyGroup>
  <ItemGroup><EmbeddedResource Include="marker.txt" LogicalName="bundle-marker" /></ItemGroup>
</Project>
'@ | Set-Content (Join-Path $fixture 'InstallerProbe.csproj')
[IO.File]::WriteAllText((Join-Path $fixture 'marker.txt'), '__NETWIV_BUNDLE_TYPE_VAR_UNK', [Text.Encoding]::ASCII)
@'
using System.Reflection;
using System.Text.Json;
using var marker = Assembly.GetExecutingAssembly().GetManifestResourceStream("bundle-marker");
Console.WriteLine(new StreamReader(marker!).ReadToEnd());
using var file = File.Create(args[0]);
using var json = new Utf8JsonWriter(file);
json.WriteStartObject();
json.WriteString("cwd", Environment.CurrentDirectory);
json.WriteStartArray("args");
foreach (var arg in args.Skip(1)) json.WriteStringValue(arg);
json.WriteEndArray();
json.WriteEndObject();
'@ | Set-Content (Join-Path $fixture 'Program.cs')
$payload = Join-Path $fixture 'target/win-x64/release'
& dotnet publish (Join-Path $fixture 'InstallerProbe.csproj') -c Release -r win-x64 -o $payload
if ($LASTEXITCODE -ne 0) { throw 'Probe NativeAOT publish failed.' }
New-Item -ItemType Directory -Path (Join-Path $payload 'data') -Force | Out-Null
Set-Content (Join-Path $payload 'data/seed.txt') 'installed payload'

function Assert($condition, [string]$message) { if (-not $condition) { throw $message } }
function Execute([string]$exe, [string]$arguments, [int[]]$codes = @(0)) {
    $process = Start-Process -FilePath $exe -ArgumentList $arguments -PassThru -Wait
    Assert ($process.ExitCode -in $codes) "$exe returned $($process.ExitCode): $arguments"
}
function Wait-Until([scriptblock]$test, [string]$message) {
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while (-not (& $test)) {
        if ([DateTime]::UtcNow -gt $deadline) { throw $message }
        Start-Sleep -Milliseconds 100
    }
}
function Install([string]$package, [string]$format, [string]$directory, [string]$log, [bool]$reject = $false) {
    if ($format -eq 'nsis') {
        $arguments = '/S'
        if ($directory) { $arguments += " /D=$directory" } # NSIS /D must be last and unquoted.
        Execute $package $arguments $(if ($reject) { @(2) } else { @(0) })
    } else {
        $arguments = "/i `"$package`" /qn /norestart /l*v `"$log`""
        if ($directory) { $arguments += " INSTALLDIR=`"$directory`"" }
        Execute 'msiexec.exe' $arguments $(if ($reject) { @(1603) } else { @(0, 3010) })
    }
}
function Remove-Installation([string]$package, [string]$format, [string]$directory, [string]$log) {
    if ($format -eq 'nsis') {
        # _?= runs in place so waiting observes the actual uninstaller, not its temp copy launcher.
        Execute (Join-Path $directory 'uninstall.exe') "/S _?=$directory"
        Remove-Item (Join-Path $directory 'uninstall.exe') -Force -ErrorAction SilentlyContinue
        if ((Test-Path $directory) -and -not (Get-ChildItem -LiteralPath $directory -Force)) { Remove-Item -LiteralPath $directory }
    } else {
        Execute 'msiexec.exe' "/x `"$package`" /qn /norestart /l*v `"$log`"" @(0, 3010)
    }
}
function Get-MsiProductCode([string]$package) {
    $database = $windowsInstaller.OpenDatabase($package, 0)
    $view = $database.OpenView('SELECT `Value` FROM `Property` WHERE `Property` = ''ProductCode''')
    try {
        [void]$view.Execute()
        $record = $view.Fetch()
        try { return $record.StringData(1) } finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record) }
    } finally {
        [void]$view.Close()
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database)
    }
}
$runtimeSubkey = 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'
$userBase = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, [Microsoft.Win32.RegistryView]::Registry32)
$machineBase = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry32)
$existingUser = $userBase.OpenSubKey($runtimeSubkey)
$existingMachine = $machineBase.OpenSubKey($runtimeSubkey)
$userKeyExisted = $null -ne $existingUser
$machineKeyExisted = $null -ne $existingMachine
if ($existingUser) { $existingUser.Dispose() }
if ($existingMachine) { $existingMachine.Dispose() }
$userRuntime = $userBase.CreateSubKey($runtimeSubkey)
$machineRuntime = $machineBase.CreateSubKey($runtimeSubkey)
$oldUser = $userRuntime.GetValue('pv')
$oldMachine = $machineRuntime.GetValue('pv')
$oldUserKind = if ($null -ne $oldUser) { $userRuntime.GetValueKind('pv') } else { $null }
$oldMachineKind = if ($null -ne $oldMachine) { $machineRuntime.GetValueKind('pv') } else { $null }
$failures = [Collections.Generic.List[string]]::new()
$windowsInstaller = New-Object -ComObject WindowsInstaller.Installer
try {
    foreach ($scope in @('currentUser', 'perMachine')) {
        foreach ($format in @('nsis', 'msi')) {
            $product = "NetWiv-CI-$scope-$format"
            $identifier = "dev.netwiv.ci.$scope.$format"
            $install = Join-Path $root "custom path $scope $format"
            $initialInstall = $install
            $report = Join-Path $root "$scope-$format-launch.json"
            $sentinel = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) "$product-user-data.txt"
            Set-Content $sentinel 'keep application data'
            New-Item -ItemType Directory -Path $install -Force | Out-Null
            $insideSentinel = Join-Path $install 'unrelated-user-file.txt'
            Set-Content $insideSentinel 'keep unrelated files'
            $desktop = [Environment]::GetFolderPath($(if ($scope -eq 'perMachine') { 'CommonDesktopDirectory' } else { 'DesktopDirectory' }))
            $programs = [Environment]::GetFolderPath($(if ($scope -eq 'perMachine') { 'CommonPrograms' } else { 'Programs' }))
            $shortcutName = "$product 작업 & 도구"
            $desktopLink = Join-Path $desktop "$shortcutName.lnk"
            $menuLink = Join-Path $programs "$product/$shortcutName.lnk"
            $workingDirectory = 'data\[work] $folder'
            $expected = @('space value', '한글', 'a"b', '', 'C:\trailing\', 'a\"b', '$INSTDIR', '[INSTALLDIR]', '$(var.foo)', '!(wix.foo)', '<&>', '!(literal)', '$(loc.literal)', '$(loc.x)', '!(loc.)', '${NSISDIR}', '50%', '$\n', ('long-' + ('x' * 280)))
            $config = @{
                productName = $product; identifier = $identifier; version = '1.0.0'
                build = @{ dotnetProject = 'InstallerProbe.csproj' }
                bundle = @{ windows = @{ wix = @{ upgradeCode = '13ad4b8c-2335-4778-9859-e970f183b9a0' }; installer = @{
                    scope = $scope; allowDirectorySelection = $true
                    shortcuts = @{ desktop = $true; startMenu = $true; name = $shortcutName; workingDirectory = $workingDirectory; arguments = @($report) + $expected }
                } } }
            }
            # Omitted scope exercises the backward-compatible default.
            if ($scope -eq 'currentUser') { $config.bundle.windows.installer.Remove('scope') }
            $package = $null
            $installed = $false
            $installedPackage = $null
            $installedDirectory = $null
            $productCodes = [Collections.Generic.List[string]]::new()
            try {
                foreach ($version in @('1.0.0', '1.0.1', '1.0.2')) {
                    if ($version -eq '1.0.2') { $install = Join-Path $root "relocated $scope $format" }
                    $obsolete = Join-Path $payload 'obsolete.txt'
                    if ($version -eq '1.0.0') { Set-Content $obsolete 'removed in the next version' } elseif (Test-Path $obsolete) { Remove-Item $obsolete }
                    $config.bundle.windows.wix.upgradeCode = if ($version -eq '1.0.0') { '13ad4b8c-2335-4778-9859-e970f183b9a0' } else { '{13AD4B8C-2335-4778-9859-E970F183B9A0}' }
                    $config.version = $version
                    $config | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $fixture 'netwiv.conf.json')
                    Push-Location $fixture
                    try {
                        & dotnet $cli bundle -t win-x64 -b $format --no-sign
                        if ($LASTEXITCODE -ne 0) { throw "Bundle failed: $scope/$format/$version" }
                    } finally { Pop-Location }
                    $pattern = if ($format -eq 'nsis') { "${product}_${version}_x64-setup.exe" } else { "${product}_${version}_x64_en-US.msi" }
                    $package = (Get-ChildItem (Join-Path $payload "bundle/$format") -Filter $pattern).FullName
                    if ($format -eq 'msi') { $productCodes.Add((Get-MsiProductCode $package)) }
                    if ($version -eq '1.0.0') {
                        # No runtime must fail. A user-only runtime must still fail for a machine install.
                        $machineRuntime.SetValue('pv', '0.0.0.0')
                        $userRuntime.SetValue('pv', '0.0.0.0')
                        Install $package $format $install (Join-Path $root "$product-missing-runtime.log") $true
                        Assert (-not (Test-Path (Join-Path $install 'InstallerProbe.exe'))) 'Rejected install wrote payload.'
                        $userRuntime.SetValue('pv', '123.0.0.0')
                        if ($scope -eq 'perMachine') {
                            Install $package $format $install (Join-Path $root "$product-user-runtime.log") $true
                            $machineRuntime.SetValue('pv', '123.0.0.0')
                        }
                    }
                    if ($version -eq '1.0.2' -and $format -eq 'nsis') {
                        $hive = if ($scope -eq 'perMachine') { 'HKLM' } else { 'HKCU' }
                        $key = "$hive`:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$identifier"
                        Set-ItemProperty $key FileScopedUninstall 0
                        try {
                            Install $package $format $install '' $true
                            Assert (Test-Path (Join-Path $initialInstall 'InstallerProbe.exe')) 'Rejected legacy relocation changed the previous installation.'
                        } finally { Set-ItemProperty $key FileScopedUninstall 1 }
                    }
                    if ($version -eq '1.0.1' -and $format -eq 'nsis') {
                        $lockedPath = Join-Path $install 'obsolete.txt'
                        $locked = [IO.File]::Open($lockedPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
                        try {
                            # A failed removal must retain its registration and ownership information for retry.
                            Execute (Join-Path $install 'uninstall.exe') "/S _?=$install" @(2)
                            Install $package $format '' '' $true
                            $hive = if ($scope -eq 'perMachine') { 'HKLM' } else { 'HKCU' }
                            $registration = Get-ItemProperty "$hive`:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$identifier"
                            Assert ($registration.DisplayVersion -eq '1.0.0') 'Locked-file upgrade replaced the previous registration.'
                            Assert (Test-Path $lockedPath) 'Locked obsolete file disappeared.'
                            Assert (Test-Path (Join-Path $install 'uninstall.exe')) 'Failed removal lost its uninstaller.'
                            Assert ((Test-Path $desktopLink) -and (Test-Path $menuLink)) 'Failed removal deleted its shortcuts.'
                        } finally { $locked.Dispose() }
                    }
                    Install $package $format $(if ($version -ne '1.0.1') { $install } else { '' }) (Join-Path $root "$product-$version-install.log")
                    $installed = $true
                    $installedPackage = $package
                    $installedDirectory = $install
                    Assert (Test-Path (Join-Path $install 'InstallerProbe.exe')) 'Upgrade lost custom installation directory.'
                    Assert ((Test-Path (Join-Path $install 'obsolete.txt')) -eq ($version -eq '1.0.0')) 'Upgrade left an obsolete package file.'
                    if ($version -eq '1.0.2') {
                        Assert (-not (Test-Path (Join-Path $initialInstall 'InstallerProbe.exe'))) 'Relocation left the old executable.'
                        Assert (-not (Test-Path (Join-Path $initialInstall 'uninstall.exe'))) 'Relocation left the old uninstaller.'
                    }
                    foreach ($link in @($desktopLink, $menuLink)) {
                        Assert (Test-Path $link) "Missing shortcut: $link"
                        $kind = if ($link -eq $desktopLink) { 'desktop' } else { 'menu' }
                        Copy-Item -LiteralPath $link -Destination (Join-Path $root "$product-$version-$kind.lnk")
                        Remove-Item $report -ErrorAction SilentlyContinue
                        # Execute the actual .lnk, not a reconstructed executable command line.
                        $launch = [Diagnostics.ProcessStartInfo]::new()
                        $launch.FileName = $link
                        $launch.UseShellExecute = $true
                        $launch.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
                        $launched = [Diagnostics.Process]::Start($launch)
                        if ($null -ne $launched) {
                            $launched.WaitForExit()
                            Assert ($launched.ExitCode -eq 0) "Shortcut failed: $($launched.ExitCode)"
                            $launched.Dispose()
                        }
                        Wait-Until { Test-Path $report } 'Shortcut did not launch the probe.'
                        $actual = Get-Content $report -Raw | ConvertFrom-Json
                        Assert ($actual.cwd -eq (Join-Path $install $workingDirectory)) 'Launched process has wrong working directory.'
                        Assert ($actual.args.Count -eq $expected.Count) 'Argument count changed.'
                        for ($i = 0; $i -lt $expected.Count; $i++) { Assert ($actual.args[$i] -ceq $expected[$i]) "Argument $i changed: $($actual.args[$i])" }
                    }
                    $hive = if ($scope -eq 'perMachine') { 'HKLM' } else { 'HKCU' }
                    if ($format -eq 'nsis') {
                        $registration = Get-ItemProperty "$hive`:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$identifier"
                        Assert ($registration.InstallLocation.TrimEnd('\') -eq $install) 'Wrong uninstall registration location.'
                        Assert ($registration.DisplayVersion -eq $version) 'Wrong uninstall registration version.'
                    } else {
                        # Query only our products: unrelated advertised/broken products can throw on InstallProperty.
                        $currentCode = $productCodes[$productCodes.Count - 1]
                        $entries = @($windowsInstaller.ProductsEx($currentCode, '', 7))
                        Assert ($entries.Count -eq 1) "MSI left $($entries.Count) product registrations."
                        Assert ($entries[0].InstallProperty('InstalledProductName') -eq $product) 'MSI registered the wrong product.'
                        Assert ($entries[0].InstallProperty('VersionString') -eq $version) 'MSI registration version did not upgrade.'
                        Assert ($entries[0].InstallProperty('InstallLocation').TrimEnd('\') -eq $install) 'MSI registration lost the install path.'
                        $expectedContext = if ($scope -eq 'perMachine') { 4 } else { 2 }
                        $scopedEntries = @($windowsInstaller.ProductsEx($currentCode, '', $expectedContext))
                        Assert ($scopedEntries.Count -eq 1) 'MSI is not registered in the requested installation context.'
                        foreach ($previousCode in $productCodes | Select-Object -SkipLast 1) {
                            Assert (@($windowsInstaller.ProductsEx($previousCode, '', 7)).Count -eq 0) 'MSI upgrade left a previous product registration.'
                        }
                    }
                }
                # Runtime removal must not prevent uninstall; no user data outside INSTDIR may be removed.
                $machineRuntime.SetValue('pv', '0.0.0.0'); $userRuntime.SetValue('pv', '0.0.0.0')
                Remove-Installation $package $format $install (Join-Path $root "$product-uninstall.log")
                $installed = $false
                Wait-Until { -not (Test-Path (Join-Path $install 'InstallerProbe.exe')) } 'Uninstall left the application.'
                Assert (-not (Test-Path $desktopLink) -and -not (Test-Path $menuLink)) 'Uninstall left shortcuts.'
                foreach ($code in $productCodes) {
                    Assert (@($windowsInstaller.ProductsEx($code, '', 7)).Count -eq 0) 'MSI uninstall left a product registration.'
                }
                Assert ((Get-Content $sentinel) -eq 'keep application data') 'Uninstall removed external application data.'
                Assert ((Get-Content -LiteralPath $insideSentinel) -eq 'keep unrelated files') 'Uninstall removed unrelated files in the chosen directory.'
                Write-Output "PASS $scope ${format}: prerequisite rejection, install, shortcut execution, upgrade path, uninstall"
            } catch {
                $failures.Add("$scope/${format}: $_")
                Write-Output "FAIL $scope/${format}: $_"
                Write-Output $_.ScriptStackTrace
            } finally {
                if ($installed -and $installedPackage) { Remove-Installation $installedPackage $format $installedDirectory (Join-Path $root "$product-cleanup.log") }
                Remove-Item $sentinel -ErrorAction SilentlyContinue
                Remove-Item -LiteralPath $install -Recurse -Force -ErrorAction SilentlyContinue
                Remove-Item -LiteralPath $initialInstall -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }
    # Same display name and payload paths must not give independent products shared MSI components.
    $independent = [Collections.Generic.List[object]]::new()
    $machineRuntime.SetValue('pv', '123.0.0.0')
    try {
        foreach ($id in @('a', 'b')) {
            $product = 'NetWiv-CI-independent'
            $directory = Join-Path $root "independent-$id"
            $report = Join-Path $root "independent-$id.json"
            $config = @{
                productName = $product; identifier = "dev.netwiv.ci.independent.$id"; version = '1.0.0'
                build = @{ dotnetProject = 'InstallerProbe.csproj' }
                bundle = @{ windows = @{ wix = @{ upgradeCode = $(if ($id -eq 'a') { '591AD768-B7CF-4773-B3CA-B9216AD0D7E1' } else { 'F46AE542-8182-40D3-AE53-17E96C1F607D' }) }; installer = @{
                    scope = 'perMachine'
                    shortcuts = @{ desktop = $false; name = "$product-$id"; arguments = @($report, $id) }
                } } }
            }
            $config | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $fixture 'netwiv.conf.json')
            Push-Location $fixture
            try {
                & dotnet $cli bundle -t win-x64 -b msi --no-sign
                if ($LASTEXITCODE -ne 0) { throw "Independent MSI bundle failed: $id" }
            } finally { Pop-Location }
            $package = Join-Path $root "independent-$id.msi"
            Copy-Item (Join-Path $payload "bundle/msi/${product}_1.0.0_x64_en-US.msi") $package -Force
            Install $package 'msi' $directory (Join-Path $root "independent-$id-install.log")
            $independent.Add(@{ Package = $package; Directory = $directory; Code = (Get-MsiProductCode $package) })
            Assert (Test-Path (Join-Path $directory 'InstallerProbe.exe')) "Independent MSI $id did not install its executable."
        }
        $first = $independent[0]
        Remove-Installation $first.Package 'msi' $first.Directory (Join-Path $root 'independent-a-uninstall.log')
        $independent.RemoveAt(0)
        Assert (@($windowsInstaller.ProductsEx($first.Code, '', 7)).Count -eq 0) 'First independent MSI remained registered.'
        Assert (-not (Test-Path (Join-Path $first.Directory 'InstallerProbe.exe'))) 'First independent MSI left its executable.'
        $second = $independent[0]
        Assert (Test-Path (Join-Path $second.Directory 'InstallerProbe.exe')) 'Removing the first MSI removed the second executable.'
        Remove-Item $report -ErrorAction SilentlyContinue
        $link = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) "$product/$product-b.lnk"
        $launch = [Diagnostics.ProcessStartInfo]::new()
        $launch.FileName = $link; $launch.UseShellExecute = $true; $launch.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
        $process = [Diagnostics.Process]::Start($launch)
        if ($null -ne $process) { $process.WaitForExit(); Assert ($process.ExitCode -eq 0) 'Independent MSI shortcut failed.'; $process.Dispose() }
        Wait-Until { Test-Path $report } 'Independent MSI shortcut did not launch.'
        $actual = Get-Content $report -Raw | ConvertFrom-Json
        Assert ($actual.cwd.TrimEnd('\') -eq $second.Directory -and $actual.args[0] -eq 'b') 'Independent MSI shortcut resolved to another product.'
        Remove-Installation $second.Package 'msi' $second.Directory (Join-Path $root 'independent-b-uninstall.log')
        $independent.Clear()
        Assert (@($windowsInstaller.ProductsEx($second.Code, '', 7)).Count -eq 0) 'Second independent MSI remained registered.'
        Assert (-not (Test-Path (Join-Path $second.Directory 'InstallerProbe.exe'))) 'Second independent MSI left its executable.'
        Assert (-not (Test-Path $link)) 'Second independent MSI left its shortcut.'
        Write-Output 'PASS independent MSI products: same display name, separate components, uninstall isolation and surviving shortcut execution'
    } finally {
        foreach ($entry in $independent) {
            if (@($windowsInstaller.ProductsEx($entry.Code, '', 7)).Count) {
                Remove-Installation $entry.Package 'msi' $entry.Directory (Join-Path $root 'independent-cleanup.log')
            }
        }
    }
    if ($failures.Count) { throw ($failures -join [Environment]::NewLine) }
} finally {
    if ($null -eq $oldUser) { $userRuntime.DeleteValue('pv', $false) } else { $userRuntime.SetValue('pv', $oldUser, $oldUserKind) }
    if ($null -eq $oldMachine) { $machineRuntime.DeleteValue('pv', $false) } else { $machineRuntime.SetValue('pv', $oldMachine, $oldMachineKind) }
    $userRuntime.Dispose(); $machineRuntime.Dispose()
    if (-not $userKeyExisted) { $userBase.DeleteSubKey($runtimeSubkey, $false) }
    if (-not $machineKeyExisted) { $machineBase.DeleteSubKey($runtimeSubkey, $false) }
    $userBase.Dispose(); $machineBase.Dispose()
}
