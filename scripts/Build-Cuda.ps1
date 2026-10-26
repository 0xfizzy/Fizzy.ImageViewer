[CmdletBinding()]
param(
    [ValidateSet(11,12,13)][int]$CudaMajor = 11,
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [string]$Architectures = '75',
    [string]$CudaRoot,
    [switch]$Release
)
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Cuda.Common.psm1" -Force
$repo = Get-Repo
$matrix = Get-CudaMatrix
$variant = Get-CudaVariant $CudaMajor
$explicitCudaRoot = ![string]::IsNullOrWhiteSpace($CudaRoot)
if (!$CudaRoot) {
    $downloaded = "$repo/artifacts/cuda/tools/cuda$CudaMajor"
    $CudaRoot = if (Test-Path "$downloaded/bin/nvcc.exe") { $downloaded } else { $env:CUDA_PATH }
    if (!$CudaRoot) {
        $command = Get-Command nvcc -ErrorAction SilentlyContinue
        if ($command) { $CudaRoot = Split-Path (Split-Path $command.Source -Parent) -Parent }
    }
}
if (!$CudaRoot -or !(Test-Path "$CudaRoot/bin/nvcc.exe")) { throw 'Specify -CudaRoot or run Setup-Cuda.ps1 -DownloadToolkit.' }
$nvccText = & "$CudaRoot/bin/nvcc.exe" --version
if ($LASTEXITCODE -or ($nvccText -join ' ') -notmatch 'V(\d+\.\d+\.\d+)') { throw 'Cannot identify nvcc.' }
$nvcc = $Matches[1]
$toolkit = "nvcc $nvcc"
if (Test-Path "$CudaRoot/version.json") {
    $toolkit = (Get-Content "$CudaRoot/version.json" -Raw | ConvertFrom-Json).cuda.version
}
if ([int]$nvcc.Split('.')[0] -ne $CudaMajor) { throw 'CUDA Toolkit major differs from -CudaMajor.' }
if (!$explicitCudaRoot -and $nvcc -cne $variant.nvcc) { throw 'Default builds require the matrix Toolkit. Use -CudaRoot explicitly for another local Toolkit of the same major.' }
if ($Release) {
    Assert-ReleaseIdentity (Get-PackageVersion)
    if ($nvcc -cne $variant.nvcc -or $toolkit -cne $variant.toolkit -or $Architectures -cne $variant.architectures) { throw 'Release requires the exact CUDA matrix baseline.' }
}
$cmake = "$repo/artifacts/cuda/tools/cmake/cmake-$($matrix.cmake.version)-windows-x86_64/bin/cmake.exe"
$ninja = "$repo/artifacts/cuda/tools/ninja/ninja.exe"
if (!(Test-Path $cmake) -or !(Test-Path $ninja)) { throw 'Run scripts/Setup-Cuda.ps1 to prepare pinned CMake and Ninja.' }
if ((& $cmake --version | Select-Object -First 1) -cne "cmake version $($matrix.cmake.version)" -or (& $ninja --version) -cne $matrix.ninja.version) { throw 'Build tool version mismatch.' }
$vswhere = "${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe"
$vs = if (Test-Path $vswhere) { & $vswhere -products '*' -requires $matrix.msvcComponent -latest -property installationPath }
if (!$vs) { throw 'MSVC 14.38 missing. Run Setup-Cuda.ps1 -InstallBuildTools from an elevated PowerShell.' }
if (!(Test-Path "$vs/Common7/Tools/VsDevCmd.bat")) { throw 'C++ Build Tools environment missing. Run Setup-Cuda.ps1 -InstallBuildTools from an elevated PowerShell.' }
if (!(Test-Path "$vs/VC/Auxiliary/Build/vcvarsall.bat")) { throw 'nvcc requires the Visual Studio C++ tools installation layout (vcvarsall.bat). Run Setup-Cuda.ps1 -InstallBuildTools from an elevated PowerShell.' }
$compiler = Get-ChildItem "$vs/VC/Tools/MSVC" -Directory | Where-Object Name -like "$($matrix.msvc).*" | Sort-Object Name -Descending | Select-Object -First 1
if (!$compiler) { throw 'MSVC 14.38 compiler directory missing.' }
$cl = (Join-Path $compiler.FullName 'bin/Hostx64/x64/cl.exe').Replace('\','/')
$CudaRoot = $CudaRoot.Replace('\','/')
$repo = $repo.Replace('\','/')
$build = "$repo/build/obj/cuda/$CudaMajor/$Configuration"
$output = "$repo/build/$Configuration/native/cuda$CudaMajor"
[void][IO.Directory]::CreateDirectory($build)
[void][IO.Directory]::CreateDirectory($output)
# A failed rebuild must not leave an apparently valid prior build record.
if (Test-Path "$output/build.json") { Remove-Item -LiteralPath "$output/build.json" }
$dirty = [bool](& git -C $repo status --porcelain)
$sourceHash = Get-CudaSourceIdentity
$commands = @(
    '@echo off',
    ('call "{0}\Common7\Tools\VsDevCmd.bat" -no_logo -arch=x64 -host_arch=x64 -winsdk={1} -vcvars_ver={2}' -f $vs,$matrix.sdk,$matrix.msvc),
    'if errorlevel 1 exit /b %errorlevel%',
    ('if not "%VCToolsVersion%"=="{0}" exit /b 2' -f $compiler.Name),
    ('if not "%WindowsSDKVersion%"=="{0}\" exit /b 3' -f $matrix.sdk),
    ('"{0}" -S "{1}/native/Cuda" -B "{2}" -G Ninja "-DCMAKE_MAKE_PROGRAM={3}" "-DCMAKE_BUILD_TYPE={4}" "-DCMAKE_CXX_COMPILER={5}" "-DCMAKE_CUDA_HOST_COMPILER=cl.exe" "-DCMAKE_CUDA_FLAGS=--use-local-env" "-DCMAKE_CUDA_COMPILER={6}/bin/nvcc.exe" "-DCUDAToolkit_ROOT={6}" "-DCMAKE_CUDA_ARCHITECTURES={7}" "-DFV_CUDA_MAJOR={8}" -DFIZZY_IMAGEVIEWER_CUDA_TESTS=ON' -f $cmake,$repo,$build,$ninja,$Configuration,$cl,$CudaRoot,$Architectures,$CudaMajor),
    'if errorlevel 1 exit /b %errorlevel%',
    ('"{0}" --build "{1}" --parallel 2' -f $cmake,$build),
    'if errorlevel 1 exit /b %errorlevel%',
    ('dumpbin /exports "{0}/Fizzy.ImageViewer.Cuda.Native.dll" > "{0}/exports.txt"' -f $output),
    'if errorlevel 1 exit /b %errorlevel%',
    ('dumpbin /dependents "{0}/Fizzy.ImageViewer.Cuda.Native.dll" > "{0}/dependencies.txt"' -f $output),
    'exit /b %errorlevel%'
)
$commandFile = "$build/build.cmd"
$commands | Set-Content $commandFile -Encoding ascii
Invoke-Checked $env:ComSpec @('/d','/c',$commandFile)
$exports = Get-Content "$output/exports.txt" -Raw
foreach ($symbol in @('fv_abi_version','fv_error','fv_error_domain','fv_error_code','fv_display_create','fv_display_write','fv_display_release','fv_display_live_surfaces','fv_query')) {
    if ($exports -notmatch "\b$symbol\b") { throw "Missing native ABI export: $symbol" }
}
$dependencies = Get-Content "$output/dependencies.txt" -Raw
if ($dependencies -match '(?i)\b(cudart\d*|vcruntime\d*|msvcp\d*)[^\s]*\.dll\b') { throw 'Native DLL unexpectedly depends on a dynamic CUDA/MSVC runtime.' }
if ($sourceHash -cne (Get-CudaSourceIdentity)) { throw 'CUDA sources changed during compilation.' }
$info = @{schema=1;major=$CudaMajor;configuration=$Configuration;commit=(Get-Commit);dirty=$dirty;sourceHash=$sourceHash;toolkit=$toolkit;nvcc=$nvcc;msvc=$compiler.Name;sdk=$matrix.sdk;cmake=$matrix.cmake.version;ninja=$matrix.ninja.version;architectures=$Architectures;nativeHash=(Get-Hash "$output/Fizzy.ImageViewer.Cuda.Native.dll")}
Write-Json $info "$output/build.json"
Write-Host "CUDA $CudaMajor native build complete: $output"
