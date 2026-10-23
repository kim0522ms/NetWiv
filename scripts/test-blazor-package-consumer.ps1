#requires -Version 7.0
param(
    [Parameter(Mandatory)][string]$Cli,
    [Parameter(Mandatory)][string]$Feed,
    [Parameter(Mandatory)][string]$Destination
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
& $Cli new BlazorExample --template blazor --source $Feed --output $Destination
if ($LASTEXITCODE -ne 0) { throw 'Packaged Blazor starter generation failed.' }
Push-Location $Destination
try {
    # Keep the real Razor/WASM SDK projects and the packaged generator/tasks.
    # The CLI unit fixture isolates MSBuild scheduling with plain SDK projects.
    foreach ($configuration in 'Debug', 'Release') {
        & dotnet build BlazorExample.sln -c $configuration --disable-build-servers -warnaserror
        if ($LASTEXITCODE -ne 0) { throw "Clean Blazor $configuration solution build failed." }
        & dotnet build BlazorExample.sln -c $configuration -t:Rebuild --no-restore --disable-build-servers -warnaserror
        if ($LASTEXITCODE -ne 0) { throw "Blazor $configuration solution rebuild failed." }
        $client = "frontend/obj/netwiv/$configuration/NetWivCommands.g.cs"
        if (!(Test-Path $client) -or !(Test-Path "frontend/bin/$configuration/*/NetWivUi.dll")) {
            throw "Blazor $configuration omitted the generated client or compiled Razor frontend."
        }
    }

    # A renamed command and a new shared DTO member must reach an ordinary Razor
    # compile without cleaning outputs, editing generated files or manual export.
    $program = Get-Content src-netwiv/Program.cs -Raw
    if (!$program.Contains('Greeting Greet(string name,')) { throw 'Blazor starter command fixture changed.' }
    $program.Replace('Greeting Greet(string name,', 'Greeting GreetUpdated(string name,').Replace('.InvokeHandler("greet")', '.InvokeHandler("greet_updated")').Replace('counter.Value.Next());', 'counter.Value.Next(), "updated");') |
        Set-Content src-netwiv/Program.cs
    $contracts = Get-Content contracts/Contracts.cs -Raw
    if (!$contracts.Contains('record Greeting(string Message, int Count);')) { throw 'Blazor shared DTO fixture changed.' }
    $contracts.Replace('record Greeting(string Message, int Count);', 'record Greeting(string Message, int Count, string Revision);') |
        Set-Content contracts/Contracts.cs
    $page = Get-Content frontend/Pages/Home.razor -Raw
    $page.Replace('Commands.GreetAsync(Name)', 'Commands.GreetUpdatedAsync(Name)').Replace('Message = result.Message;', 'Message = result.Revision == "updated" ? result.Message : throw new InvalidOperationException("Stale shared DTO");') |
        Set-Content frontend/Pages/Home.razor
    foreach ($configuration in 'Debug', 'Release') {
        & dotnet build frontend/NetWivUi.csproj -c $configuration --no-restore --disable-build-servers -warnaserror
        if ($LASTEXITCODE -ne 0) { throw "Blazor $configuration command/DTO regeneration failed." }
        $client = Get-Content "frontend/obj/netwiv/$configuration/NetWivCommands.g.cs" -Raw
        if (!$client.Contains('GreetUpdatedAsync(') -or $client.Contains(' GreetAsync(')) {
            throw "Blazor $configuration retained the stale command client."
        }
    }

    # Application test projects reference both the executable and shared contracts.
    New-Item -ItemType Directory consumer-tests | Out-Null
    @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../src-netwiv/BlazorExample.csproj" />
    <ProjectReference Include="../contracts/Contracts.csproj" />
  </ItemGroup>
</Project>
'@ | Set-Content consumer-tests/Consumer.Tests.csproj
    @'
using System.Text.Json;
var greeting = new Greeting("shared", 1, "updated");
var json = JsonSerializer.Serialize(greeting, AppJsonContext.Default.Greeting);
if (JsonSerializer.Deserialize(json, AppJsonContext.Default.Greeting) != greeting)
    throw new Exception("Shared DTO JSON context failed in the test project.");
Console.WriteLine("PASS packaged Blazor test project: backend " + nameof(Commands.GreetUpdated) + " and shared JSON contract");
'@ | Set-Content consumer-tests/Program.cs
    & dotnet run --project consumer-tests/Consumer.Tests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Packaged Blazor application test project failed.' }

    foreach ($mode in 'managed', 'aot') {
        $arguments = @('build', '--no-bundle')
        if ($mode -eq 'aot') { $arguments += '--aot' }
        else { $arguments += '--framework-dependent' }
        & $Cli @arguments
        if ($LASTEXITCODE -ne 0) { throw "Packaged Blazor $mode starter publish failed." }
        $index = Get-Content dist/wwwroot/index.html -Raw
        if ($index -notmatch 'src="([^"]*blazor\.webassembly[^"]*\.js)"' -or !(Test-Path (Join-Path 'dist/wwwroot' $Matches[1]))) {
            throw "Blazor $mode published HTML does not resolve its SDK bootstrap script."
        }
        if ((Get-Content frontend/wwwroot/index.html -Raw).Contains('type="importmap"')) {
            if ($index -notmatch '(?s)<script\b[^>]*type="importmap"[^>]*>\s*(\{.*?\})\s*</script>' -or
                @((($Matches[1] | ConvertFrom-Json).imports).PSObject.Properties).Count -eq 0) {
                throw "Blazor $mode published HTML omitted its SDK import map."
            }
        }
        $binaryName = 'BlazorExample' + $(if ($IsWindows) { '.exe' } else { '' })
        $published = @(Get-ChildItem target -Recurse -File | Where-Object { $_.Name -eq $binaryName -and $_.Directory.Name -eq 'release' })
        if ($published.Count -ne 1) { throw 'Expected one published Blazor apphost.' }
        $binary = $published[0].FullName
        if ($IsWindows) {
            # Give each run its own WebView profile while the next mode cleans target/.
            $execution = Join-Path (Split-Path $Destination) "blazor-$mode"
            Copy-Item -LiteralPath (Split-Path $binary) -Destination $execution -Recurse
            $binary = Join-Path $execution $binaryName
        }
        if ($IsLinux) {
            $output = @(& timeout --kill-after=10s 180s xvfb-run -a bash (Join-Path $repository 'scripts/run-linux-smoke-desktop.sh') $binary x11 --smoke-test)
        }
        else { $output = @(& $binary --smoke-test) }
        $code = $LASTEXITCODE
        $output | Write-Output
        if ($code -ne 0 -or !($output -contains 'PASS: Blazor form → generated C# → JSON')) {
            throw "Packaged Blazor $mode generated client/DTO IPC smoke failed."
        }
        Write-Output "PASS packaged Blazor ${mode}: real Razor/WASM, regenerated command, shared DTO and GUI/IPC"
    }
}
finally { Pop-Location }
