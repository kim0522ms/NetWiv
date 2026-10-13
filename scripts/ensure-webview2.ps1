#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$client = '{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'
$keys = @(
    "HKCU:\Software\Microsoft\EdgeUpdate\Clients\$client",
    "HKLM:\Software\WOW6432Node\Microsoft\EdgeUpdate\Clients\$client",
    "HKLM:\Software\Microsoft\EdgeUpdate\Clients\$client"
)
function Get-RuntimeVersion {
    foreach ($key in $keys) {
        $registration = Get-Item -LiteralPath $key -ErrorAction SilentlyContinue
        if ($null -eq $registration) { continue }
        try { $version = $registration.GetValue('pv') } finally { $registration.Dispose() }
        if ($version -and $version -ne '0.0.0.0') { return $version }
    }
}
if (-not (Get-RuntimeVersion)) {
    $installer = Join-Path $env:RUNNER_TEMP 'MicrosoftEdgeWebview2Setup.exe'
    Invoke-WebRequest 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $installer
    $signature = Get-AuthenticodeSignature -LiteralPath $installer
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
        throw 'WebView2 installer signature verification failed.'
    }
    $process = Start-Process -FilePath $installer -ArgumentList '/silent', '/install' -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0) { throw "WebView2 installation failed: $($process.ExitCode)" }
}
$version = Get-RuntimeVersion
if (-not $version) { throw 'WebView2 Runtime is still missing.' }
Write-Output "WebView2 Runtime: $version"
