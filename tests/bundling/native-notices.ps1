param([Parameter(Mandatory)][string]$Package)
$ErrorActionPreference = 'Stop'
$taskPackagePath = (Resolve-Path -LiteralPath $Package).Path
$taskArchive = [IO.Compression.ZipFile]::OpenRead($taskPackagePath)
function Read-PackageBytes([string]$Name) {
    $taskEntry = $taskArchive.GetEntry($Name)
    if ($null -eq $taskEntry) { throw "Missing package entry: $Name" }
    $taskStream = $taskEntry.Open()
    $taskBuffer = [IO.MemoryStream]::new()
    try { $taskStream.CopyTo($taskBuffer); return ,$taskBuffer.ToArray() }
    finally { $taskStream.Dispose(); $taskBuffer.Dispose() }
}
function Get-BytesHash([byte[]]$Bytes) {
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant()
}
try {
    $taskInventory = [Text.Encoding]::UTF8.GetString((Read-PackageBytes 'native-notices/manifest.json')) | ConvertFrom-Json
    $taskTextHashes = [Collections.Generic.HashSet[string]]::new()
    $taskReferenceCount = 0
    foreach ($taskDependency in $taskInventory.packages) {
        if ($taskDependency.files.Count -eq 0) { throw "Missing notice: $($taskDependency.name)" }
        foreach ($taskFile in $taskDependency.files) {
            $taskBytes = Read-PackageBytes "native-notices/texts/$($taskFile.sha256).txt"
            if ((Get-BytesHash $taskBytes) -ne $taskFile.sha256 -or $taskBytes.Length -ne $taskFile.bytes) {
                throw "Notice integrity mismatch: $($taskDependency.name)/$($taskFile.path)"
            }
            if ($null -ne $taskFile.sourceProof) {
                $taskProof = Read-PackageBytes "native-notices/$($taskFile.sourceProof.path)"
                if ((Get-BytesHash $taskProof) -ne $taskFile.sourceProof.sha256) { throw 'Source proof integrity mismatch' }
            }
            $null = $taskTextHashes.Add($taskFile.sha256)
            $taskReferenceCount++
        }
    }
    if ($taskTextHashes.Count -ne $taskInventory.uniqueTextCount) { throw 'Unique notice count mismatch' }
    if ($taskInventory.packages.Count -ne $taskInventory.packageCount) { throw 'Dependency count mismatch' }
    $taskNativeHashes = @{}
    foreach ($taskFile in @('runtimes/win-x64/native/netwiv_native.dll', 'runtimes/win-x64/native/WebView2Loader.dll', 'runtimes/linux-x64/native/libnetwiv_native.so')) {
        $taskNativeHashes[$taskFile] = Get-BytesHash (Read-PackageBytes $taskFile)
    }
    [pscustomobject]@{ package = $taskPackagePath; packageSha256 = (Get-FileHash -LiteralPath $taskPackagePath -Algorithm SHA256).Hash.ToLowerInvariant();
        dependencies = $taskInventory.packageCount; uniqueTexts = $taskTextHashes.Count; textReferences = $taskReferenceCount;
        nativeHashes = $taskNativeHashes; status = 'passed' } | ConvertTo-Json -Depth 5
}
finally { $taskArchive.Dispose() }
