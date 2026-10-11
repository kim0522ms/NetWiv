param([string]$OutputDirectory = 'artifacts/beta/0.1.0-beta.1')
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$destination = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $repository $OutputDirectory }
$feed = Join-Path $destination 'packages'
New-Item -ItemType Directory -Path $feed -Force | Out-Null
foreach ($project in @('NetWiv.Core', 'NetWiv.Generators', 'NetWiv.Build', 'NetWiv.Platform.Desktop')) {
    & dotnet pack (Join-Path $repository "src/$project/$project.csproj") -c Release -p:Version=0.1.0-beta.1 -o $feed
    if ($LASTEXITCODE -ne 0) { throw "Packing $project failed." }
}
$starter = Join-Path $destination 'Starter'
New-Item -ItemType Directory -Path $starter -Force | Out-Null
Copy-Item -Path (Join-Path $repository 'templates/beta-app/*') -Destination $starter -Recurse -Force
$vendor = Join-Path $starter 'wwwroot/vendor/@tauri-apps'
New-Item -ItemType Directory -Path $vendor -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repository 'samples/NetWiv.SampleApp/wwwroot/vendor/@tauri-apps/api') -Destination $vendor -Recurse -Force
Copy-Item -LiteralPath (Join-Path $repository 'THIRD-PARTY-NOTICES.md') -Destination $destination
Copy-Item -LiteralPath (Join-Path $repository 'NOTICE') -Destination $destination
Copy-Item -Path (Join-Path $repository 'LICENSE-*') -Destination $destination
Copy-Item -LiteralPath (Join-Path $repository 'docs/beta-quickstart.md') -Destination (Join-Path $destination 'README.md')
@'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="netwiv-beta" value="packages" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
'@ | Set-Content -LiteralPath (Join-Path $destination 'NuGet.Config') -Encoding utf8
Get-ChildItem -LiteralPath $feed -Filter '*.nupkg' | Get-FileHash -Algorithm SHA256 |
    Select-Object @{Name='File';Expression={Split-Path $_.Path -Leaf}}, Hash |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $destination 'package-hashes.json') -Encoding utf8
Write-Output "Beta kit: $destination"
