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
    Write-Host "Preparing $name $($matrix.$name.version)..."
    $zip = Get-CudaDownload $matrix.$name $cache
    $dest = Reset-Output "$root/$name"
    Expand-SafeZip $zip $dest
}
if ($DownloadToolkit) {
    $dest = Reset-Output "$root/cuda$CudaMajor"
    foreach ($asset in $variant.archives) {
        Write-Host "Preparing CUDA $CudaMajor component $($asset.component)..."
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
    $vswhere = "${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe"
    $ready = if (Test-Path $vswhere) { & $vswhere -version '[17.0,18.0)' -products '*' -requires $matrix.msvcComponent $matrix.sdkComponent -latest -property installationPath }
    if ($ready -and (!(Test-Path "$ready/Common7/Tools/VsDevCmd.bat") -or !(Test-Path "$ready/VC/Auxiliary/Build/vcvarsall.bat"))) { $ready = $null }
    if ($ready) {
        Write-Host "Pinned MSVC and SDK already installed: $ready"
        return
    }
    [void][IO.Directory]::CreateDirectory($cache)
    $installer = Join-Path $cache 'vs_BuildTools.exe'
    [void](Get-RemoteFile $matrix.visualStudioBootstrapper $installer)
    $signature = Get-AuthenticodeSignature $installer
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Microsoft Corporation') { throw 'Invalid Microsoft installer signature.' }
    $existing = if (Test-Path $vswhere) { & $vswhere -version '[17.0,18.0)' -products '*' -latest -property installationPath }
    $arguments = @('--quiet','--wait','--norestart','--nocache')
    if ($existing) { $arguments = @('modify') + $arguments + @('--installPath', ('"' + $existing + '"')) }
    else { $arguments += @('--installPath','C:\BuildTools2022') }
    $arguments += @('--add',$matrix.msvcCoreComponent,'--add',$matrix.msvcEnvironmentComponent,'--add',$matrix.msvcComponent,'--add',$matrix.sdkComponent)
    Write-Host "Installing MSVC $($matrix.msvc) and SDK $($matrix.sdk). This silent installation can take several minutes; logs: $env:TEMP/dd_setup*.log"
    $process = Start-Process $installer -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -notin @(0,3010)) { throw "Build Tools installer failed: $($process.ExitCode)" }
    if ($process.ExitCode -eq 3010) { Write-Warning 'Build Tools requires a reboot before local validation.' }
    $installed = & $vswhere -version '[17.0,18.0)' -products '*' -requires $matrix.msvcComponent $matrix.sdkComponent -latest -property installationPath
    if (!$installed -or !(Test-Path "$installed/Common7/Tools/VsDevCmd.bat") -or !(Test-Path "$installed/VC/Auxiliary/Build/vcvarsall.bat")) { throw 'Build Tools installation did not provide the requested compiler, SDK and C++ environment scripts.' }
}
Write-Host 'CUDA build tools prepared. No NVIDIA driver or persistent environment variables were changed.'
