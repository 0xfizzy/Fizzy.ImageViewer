param([ValidateSet(11,12,13)][int]$CudaMajor, [string]$Configuration, [string]$Directory, [switch]$Release)
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Cuda.Common.psm1" -Force
$null = Assert-CudaNative $CudaMajor $Configuration $Directory -Release:$Release
Write-Host "PASS: CUDA $CudaMajor native identity and checksum."
