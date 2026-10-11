# Runs only on a disposable, elevated Windows CI runner: prerequisite tests temporarily change WebView2 registration.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows -or $env:CI -ne 'true') { throw 'Use a disposable Windows CI runner.' }
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
        if (Test-Path $directory) { Remove-Item $directory -ErrorAction SilentlyContinue }
    } else {
        Execute 'msiexec.exe' "/x `"$package`" /qn /norestart /l*v `"$log`"" @(0, 3010)
    }
}
$runtimeSubkey = 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'
$userBase = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, [Microsoft.Win32.RegistryView]::Registry32)
$machineBase = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry32)
$userRuntime = $userBase.CreateSubKey($runtimeSubkey)
$machineRuntime = $machineBase.CreateSubKey($runtimeSubkey)
$oldUser = $userRuntime.GetValue('pv')
$oldMachine = $machineRuntime.GetValue('pv')
$shell = New-Object -ComObject WScript.Shell
try {
    foreach ($scope in @('currentUser', 'perMachine')) {
        foreach ($format in @('nsis', 'msi')) {
            $product = "NetWiv-CI-$scope-$format"
            $identifier = "dev.netwiv.ci.$scope.$format"
            $install = Join-Path $root "custom path $scope $format"
            $report = Join-Path $root "$scope-$format-launch.json"
            $sentinel = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) "$product-user-data.txt"
            Set-Content $sentinel 'keep application data'
            $desktop = [Environment]::GetFolderPath($(if ($scope -eq 'perMachine') { 'CommonDesktopDirectory' } else { 'DesktopDirectory' }))
            $programs = [Environment]::GetFolderPath($(if ($scope -eq 'perMachine') { 'CommonPrograms' } else { 'Programs' }))
            $shortcutName = "$product 작업 & 도구"
            $desktopLink = Join-Path $desktop "$shortcutName.lnk"
            $menuLink = Join-Path $programs "$product/$shortcutName.lnk"
            $expected = @('space value', '한글', 'a"b', '', 'C:\trailing\', 'a\"b', '$INSTDIR', '[INSTALLDIR]', '$(var.foo)', '!(wix.foo)', '<&>')
            $config = @{
                productName = $product; identifier = $identifier; version = '1.0.0'
                build = @{ dotnetProject = 'InstallerProbe.csproj' }
                bundle = @{ windows = @{ installer = @{
                    scope = $scope; allowDirectorySelection = $true
                    shortcuts = @{ desktop = $true; startMenu = $true; name = $shortcutName; workingDirectory = 'data'; arguments = @($report) + $expected }
                } } }
            }
            # Omitted scope exercises the backward-compatible default.
            if ($scope -eq 'currentUser') { $config.bundle.windows.installer.Remove('scope') }
            $package = $null
            $installed = $false
            try {
                foreach ($version in @('1.0.0', '1.0.1')) {
                    $config.version = $version
                    $config | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $fixture 'netwiv.conf.json')
                    Push-Location $fixture
                    try {
                        & dotnet $cli bundle -t win-x64 -b $format --no-sign
                        if ($LASTEXITCODE -ne 0) { throw "Bundle failed: $scope/$format/$version" }
                    } finally { Pop-Location }
                    $pattern = if ($format -eq 'nsis') { "*_${version}_x64-setup.exe" } else { "*_${version}_x64_en-US.msi" }
                    $package = (Get-ChildItem (Join-Path $payload "bundle/$format") -Filter $pattern).FullName
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
                    Install $package $format $(if ($version -eq '1.0.0') { $install } else { '' }) (Join-Path $root "$product-$version-install.log")
                    $installed = $true
                    Assert (Test-Path (Join-Path $install 'InstallerProbe.exe')) 'Upgrade lost custom installation directory.'
                    foreach ($link in @($desktopLink, $menuLink)) {
                        Assert (Test-Path $link) "Missing shortcut: $link"
                        $shortcut = $shell.CreateShortcut($link)
                        Assert ([IO.Path]::GetFullPath($shortcut.WorkingDirectory).TrimEnd('\') -eq (Join-Path $install 'data')) 'Wrong shortcut working directory.'
                        Remove-Item $report -ErrorAction SilentlyContinue
                        # Execute the actual .lnk, not a reconstructed executable command line.
                        $exitCode = $shell.Run(('"' + $link + '"'), 0, $true)
                        Assert ($exitCode -eq 0) "Shortcut failed: $exitCode"
                        Wait-Until { Test-Path $report } 'Shortcut did not launch the probe.'
                        $actual = Get-Content $report -Raw | ConvertFrom-Json
                        Assert ($actual.cwd -eq (Join-Path $install 'data')) 'Launched process has wrong working directory.'
                        Assert ($actual.args.Count -eq $expected.Count) 'Argument count changed.'
                        for ($i = 0; $i -lt $expected.Count; $i++) { Assert ($actual.args[$i] -ceq $expected[$i]) "Argument $i changed: $($actual.args[$i])" }
                    }
                    $hive = if ($scope -eq 'perMachine') { 'HKLM' } else { 'HKCU' }
                    if ($format -eq 'nsis') {
                        $registration = Get-ItemProperty "$hive`:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$identifier"
                        Assert ($registration.InstallLocation.TrimEnd('\') -eq $install) 'Wrong uninstall registration location.'
                        Assert ($registration.DisplayVersion -eq $version) 'Wrong uninstall registration version.'
                    } else {
                        $entries = @(Get-ChildItem "$hive`:\Software\Microsoft\Windows\CurrentVersion\Uninstall" |
                            Get-ItemProperty | Where-Object { $_.PSObject.Properties['DisplayName'] -and $_.DisplayName -eq $product })
                        Assert ($entries.Count -eq 1) 'MSI upgrade left missing or duplicate registrations.'
                        Assert ($entries[0].DisplayVersion -eq $version) 'MSI registration version did not upgrade.'
                        Assert ($entries[0].InstallLocation.TrimEnd('\') -eq $install) 'MSI registration lost the install path.'
                    }
                }
                # Runtime removal must not prevent uninstall; no user data outside INSTDIR may be removed.
                $machineRuntime.SetValue('pv', '0.0.0.0'); $userRuntime.SetValue('pv', '0.0.0.0')
                Remove-Installation $package $format $install (Join-Path $root "$product-uninstall.log")
                $installed = $false
                Wait-Until { -not (Test-Path (Join-Path $install 'InstallerProbe.exe')) } 'Uninstall left the application.'
                Assert (-not (Test-Path $desktopLink) -and -not (Test-Path $menuLink)) 'Uninstall left shortcuts.'
                Assert ((Get-Content $sentinel) -eq 'keep application data') 'Uninstall removed external application data.'
                Write-Output "PASS $scope $format: prerequisite rejection, install, shortcut execution, upgrade path, uninstall"
            } finally {
                if ($installed -and $package) { Remove-Installation $package $format $install (Join-Path $root "$product-cleanup.log") }
                Remove-Item $sentinel -ErrorAction SilentlyContinue
            }
        }
    }
} finally {
    if ($null -eq $oldUser) { $userRuntime.DeleteValue('pv', $false) } else { $userRuntime.SetValue('pv', $oldUser) }
    if ($null -eq $oldMachine) { $machineRuntime.DeleteValue('pv', $false) } else { $machineRuntime.SetValue('pv', $oldMachine) }
    $userRuntime.Dispose(); $machineRuntime.Dispose(); $userBase.Dispose(); $machineBase.Dispose()
}
