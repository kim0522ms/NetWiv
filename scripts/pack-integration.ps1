#requires -Version 7.0
param(
    [Parameter(Mandatory)][ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$')][string]$Version,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
if (!$OutputDirectory) { $OutputDirectory = "artifacts/integration/$Version" }
$destination = [IO.Path]::GetFullPath($OutputDirectory, $repository)
$feed = Join-Path $destination 'packages'
if (Test-Path -LiteralPath $destination) { throw "Use a fresh output directory: $destination" }
New-Item -ItemType Directory -Path $feed -Force | Out-Null

# Build all four native RIDs explicitly before packing; consumers never need Cargo.
$projects = @('NetWiv.Core', 'NetWiv.Generators', 'NetWiv.Build', 'NetWiv.Platform.Desktop', 'NetWiv.Cli')
$projects += Get-ChildItem -LiteralPath (Join-Path $repository 'src') -Directory -Filter 'NetWiv.Plugin.*' |
    Sort-Object Name | Select-Object -ExpandProperty Name
foreach ($project in $projects) {
    $log = Join-Path $destination "$project.log"
    & dotnet pack (Join-Path $repository "src/$project/$project.csproj") -c Release "-p:Version=$Version" -nodeReuse:false -o $feed *> $log
    if ($LASTEXITCODE -ne 0) { throw "Packing $project failed; see $log" }
    Write-Output "Packed $project $Version"
}
$packages = @(Get-ChildItem -LiteralPath $feed -Filter '*.nupkg')
if ($packages.Count -ne $projects.Count) { throw "Expected $($projects.Count) packages, found $($packages.Count)." }
$packages | Get-FileHash -Algorithm SHA256 |
    Select-Object @{Name='File';Expression={Split-Path $_.Path -Leaf}}, Hash |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $destination 'package-hashes.json') -Encoding utf8
Write-Output "Integration feed: $feed"
