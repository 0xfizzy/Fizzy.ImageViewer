param(
    [ValidateSet('Debug','Release')] [string]$Configuration = 'Release',
    [string]$Architectures = '75',
    [string]$CudaRoot = $env:CUDA_PATH
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $CudaRoot) { $CudaRoot = Split-Path -Parent (Split-Path -Parent (Get-Command nvcc -ErrorAction Stop).Source) }
$build = Join-Path $repo 'build/obj/cuda'
& cmake -S (Join-Path $repo 'native/Cuda') -B $build -G 'Visual Studio 17 2022' -A x64 -T "v142,cuda=$($CudaRoot.Replace('\','/'))" "-DCMAKE_CUDA_ARCHITECTURES=$Architectures" -DFIZZY_IMAGEVIEWER_CUDA_TESTS=ON
if ($LASTEXITCODE) { throw 'CUDA configure failed.' }
& cmake --build $build --config $Configuration --parallel 2
if ($LASTEXITCODE) { throw 'CUDA build failed.' }
# Compile only. Hardware execution requires explicit authorization.
