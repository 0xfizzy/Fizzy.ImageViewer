[CmdletBinding()]
param([ValidateSet(11,12,13)][int]$CudaMajor = 11, [switch]$DownloadToolkit, [switch]$InstallBuildTools)
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Cuda.Common.psm1" -Force
$repo = Get-Repo
$matrix = Get-CudaMatrix
$variant = Get-CudaVariant $CudaMajor
$root = Join-Path $repo 'artifacts/cuda/tools'
$cache = Join-Path $repo 'artifacts/cuda/downloads'
foreach ($name in @('cmake','ninja')) {
    $zip = Get-CudaDownload $matrix.$name $cache
    $dest = Reset-Output "$root/$name"
    Expand-SafeZip $zip $dest
}
if ($DownloadToolkit) {
    $dest = Reset-Output "$root/cuda$CudaMajor"
    foreach ($asset in $variant.archives) {
        $zip = Get-CudaDownload $asset $cache
        $unpack = Reset-Output "$root/unpack-$CudaMajor"
        Expand-SafeZip $zip $unpack
        $dirs = @(Get-ChildItem $unpack -Directory)
        if ($dirs.Count -ne 1) { throw 'Unexpected NVIDIA archive layout.' }
        Get-ChildItem $dirs[0].FullName | Copy-Item -Destination $dest -Recurse -Force
    }
    Write-Json @{cuda = @{version=$variant.toolkit}} "$dest/version.json"
    Write-Host "CUDA root: $dest"
}
if ($InstallBuildTools) {
    [void][IO.Directory]::CreateDirectory($cache)
    $installer = Join-Path $cache 'vs_BuildTools.exe'
    [void](Get-RemoteFile $matrix.visualStudioBootstrapper $installer)
    $signature = Get-AuthenticodeSignature $installer
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Microsoft Corporation') { throw 'Invalid Microsoft installer signature.' }
    $vswhere = "${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe"
    $existing = if (Test-Path $vswhere) { & $vswhere -version '[17.0,18.0)' -products '*' -latest -property installationPath }
    $arguments = @('--quiet','--wait','--norestart','--nocache')
    if ($existing) { $arguments = @('modify') + $arguments + @('--installPath', ('"' + $existing + '"')) }
    else { $arguments += @('--installPath','C:\BuildTools2022') }
    $arguments += @('--add',$matrix.msvcComponent,'--add',$matrix.sdkComponent)
    $process = Start-Process $installer -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -notin @(0,3010)) { throw "Build Tools installer failed: $($process.ExitCode)" }
    if ($process.ExitCode -eq 3010) { Write-Warning 'Build Tools requires a reboot before local validation.' }
}
Write-Host 'CUDA build tools prepared. No NVIDIA driver or persistent environment variables were changed.'

