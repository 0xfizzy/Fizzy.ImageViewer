param([Parameter(Mandatory)][string]$Package, [Parameter(Mandatory)][string]$PackageId, [Parameter(Mandatory)][string]$Version, [Parameter(Mandatory)][string]$NativeHash)
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Release.Common.psm1"
$zip = [IO.Compression.ZipFile]::OpenRead((Resolve-FullPath $Package))
try {
    $spec = @($zip.Entries | Where-Object FullName -like '*.nuspec')
    if ($spec.Count -ne 1) { throw 'Expected one CUDA nuspec.' }
    $reader = [IO.StreamReader]::new($spec[0].Open())
    try { [xml]$metadata = $reader.ReadToEnd() } finally { $reader.Dispose() }
    if ($metadata.package.metadata.id -cne $PackageId -or $metadata.package.metadata.version -cne $Version) { throw 'CUDA package metadata mismatch.' }
    $dependencies = @($metadata.package.metadata.dependencies.group.dependency | Where-Object id -eq 'Fizzy.ImageViewer')
    if ($dependencies.Count -ne 1 -or $dependencies[0].version -cne $Version) { throw 'CUDA must depend on the matching core version.' }
    $expected = @('lib/net8.0-windows7.0/Fizzy.ImageViewer.Cuda.dll','lib/net8.0-windows7.0/Fizzy.ImageViewer.Cuda.xml','runtimes/win-x64/native/Fizzy.ImageViewer.Cuda.Native.dll')
    if ($PackageId -ne 'Fizzy.ImageViewer.Cuda') { $expected += "buildTransitive/$PackageId.targets" }
    foreach ($name in $expected) {
        $entries = @($zip.Entries | Where-Object FullName -CEQ $name)
        if ($entries.Count -ne 1 -or $entries[0].Length -eq 0) { throw "Missing or duplicate CUDA package entry: $name" }
    }
    if (@($zip.Entries | Where-Object FullName -match '^runtimes/.+\.dll$').Count -ne 1) { throw 'Expected one native runtime DLL.' }
    $native = $zip.GetEntry('runtimes/win-x64/native/Fizzy.ImageViewer.Cuda.Native.dll').Open()
    try { $actual = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($native)).ToLowerInvariant() } finally { $native.Dispose() }
    if ($actual -cne $NativeHash) { throw 'CUDA package native binary differs from validation.' }
} finally { $zip.Dispose() }
Write-Host "PASS: $PackageId package payload."
