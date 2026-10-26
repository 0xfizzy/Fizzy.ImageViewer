Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Release.Common.psm1"
function Get-CudaMatrix { Get-Content "$PSScriptRoot/../eng/cuda-matrix.json" -Raw | ConvertFrom-Json }
function Get-CudaVariant([int]$Major) {
    $variant = (Get-CudaMatrix).variants | Where-Object major -eq $Major
    if (!$variant) { throw "Unsupported CUDA major: $Major" }
    $variant
}
function Get-CudaSourceIdentity {
    $repo = Get-Repo
    $files = @(& git -C $repo ls-files --cached --others --exclude-standard -- native/Cuda src/Fizzy.ImageViewer.Cuda eng/cuda-matrix.json scripts/Build-Cuda.ps1 scripts/Cuda.Common.psm1)
    if ($LASTEXITCODE) { throw 'Cannot enumerate CUDA sources.' }
    $entries = @($files | Sort-Object -Unique | ForEach-Object {
        if (Test-Path "$repo/$_") { "$_=$(Get-Hash "$repo/$_")" } else { "$_=deleted" }
    })
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($entries -join "`n")))).ToLowerInvariant()
}
function Get-CudaDownload($Asset, [string]$Directory) {
    [void][IO.Directory]::CreateDirectory($Directory)
    $file = Join-Path $Directory ($Asset.sha256 + '.zip')
    if (!(Test-Path $file) -or (Get-Hash $file) -cne $Asset.sha256) { [void](Get-RemoteFile $Asset.url $file) }
    if ((Get-Hash $file) -cne $Asset.sha256) { throw "Download checksum mismatch: $($Asset.url)" }
    $file
}
function Assert-CudaNative([int]$Major, [string]$Configuration, [string]$Directory, [switch]$Release) {
    $dll = Join-Path $Directory 'Fizzy.ImageViewer.Cuda.Native.dll'
    $record = Join-Path $Directory 'build.json'
    if (!(Test-Path $dll) -or !(Test-Path $record)) { throw 'CUDA native DLL and build record are required.' }
    $info = Get-Content $record -Raw | ConvertFrom-Json
    if ($info.schema -ne 1 -or $info.major -ne $Major -or $info.configuration -cne $Configuration) { throw 'CUDA native variant/configuration mismatch.' }
    if ($info.nativeHash -cne (Get-Hash $dll)) { throw 'CUDA native checksum mismatch.' }
    if ($info.commit -cne (Get-Commit) -or $info.sourceHash -cne (Get-CudaSourceIdentity)) { throw 'CUDA native source identity mismatch; rebuild the selected variant.' }
    if ($Release) {
        Assert-ReleaseIdentity (Get-PackageVersion)
        $variant = Get-CudaVariant $Major
        if ($info.dirty -or $info.nvcc -cne $variant.nvcc -or $info.architectures -cne $variant.architectures) { throw 'Release requires clean sources and the exact CUDA matrix baseline.' }
    }
    $info
}
Export-ModuleMember -Function *
