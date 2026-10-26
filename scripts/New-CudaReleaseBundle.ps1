[CmdletBinding()]
param([Parameter(Mandatory)][string]$Tag, [string]$InputDirectory='artifacts/cuda-variants', [string]$Output='artifacts/cuda-release-bundle')
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Cuda.Common.psm1" -Force
Import-Module "$PSScriptRoot/Cuda.Release.psm1" -Force
if ($Tag -cnotmatch '^v(.+)$') { throw 'Expected a version tag.' }
$version=$Matches[1]
[void](Get-Version $version)
$commit=& git -C (Get-Repo) rev-parse "refs/tags/$Tag^{commit}"
if($LASTEXITCODE){throw 'Missing source tag.'}
$bundle=Reset-Output $Output
$packages=@()
foreach($major in @(11,12,13)) {
    $source=Join-Path $InputDirectory "cuda-$major"
    if(!(Test-Path "$source/validation.json")){throw "Missing validated CUDA $major artifact."}
    Copy-Item $source "$bundle/cuda-$major" -Recurse
    $report=Get-Content "$source/validation.json" -Raw | ConvertFrom-Json
    $build=Get-Content "$source/build.json" -Raw | ConvertFrom-Json
    $variant=Get-CudaVariant $major
    $matrix=Get-CudaMatrix
    if($build.nvcc -cne $variant.nvcc -or $build.toolkit -cne $variant.toolkit -or $build.architectures -cne $variant.architectures -or $build.msvc -notlike "$($matrix.msvc).*" -or $build.sdk -cne $matrix.sdk -or $build.cmake -cne $matrix.cmake.version -or $build.ninja -cne $matrix.ninja.version){throw 'CUDA release toolchain differs from matrix.'}
    $packages+=@{major=$major;packageId="Fizzy.ImageViewer.Cuda$major";path="cuda-$major/packages/Fizzy.ImageViewer.Cuda$major.$version.nupkg";nativeHash=$report.nativeHash}
}
Write-Json @{schema=2;tag=$Tag;version=$version;commit=$commit;packages=$packages;files=(Get-Tree $bundle)} "$bundle/manifest.json"
$null=Assert-CudaBundle $bundle $Tag
Write-Host "CUDA release bundle ready: $bundle"
