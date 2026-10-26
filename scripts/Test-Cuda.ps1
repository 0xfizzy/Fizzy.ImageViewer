[CmdletBinding()]
param(
    [ValidateSet(11,12,13)][int]$CudaMajor = 11,
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [string]$CudaRoot,
    [switch]$RunHardwareTests,
    [switch]$Release
)
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Cuda.Common.psm1" -Force
$repo = Get-Repo
$output = Reset-Output "$repo/artifacts/cuda/$CudaMajor"
Start-Transcript "$output/validation.log" | Out-Null
Push-Location $repo
try {
    $version = Get-PackageVersion
    $id = "Fizzy.ImageViewer.Cuda$CudaMajor"
    & "$PSScriptRoot/Build-Cuda.ps1" -CudaMajor $CudaMajor -Configuration $Configuration -CudaRoot $CudaRoot -Release:$Release
    $nativeDir = "$repo/build/$Configuration/native/cuda$CudaMajor"
    $info = Assert-CudaNative $CudaMajor $Configuration $nativeDir -Release:$Release
    Invoke-Checked dotnet @('restore','tests/Fizzy.ImageViewer.Tests/Fizzy.ImageViewer.Tests.csproj','-p:RestorePackagesWithLockFile=false',"-p:CudaMajor=$CudaMajor")
    foreach ($framework in @('net8.0-windows','net10.0-windows')) {
        Invoke-Checked dotnet @('test','tests/Fizzy.ImageViewer.Tests/Fizzy.ImageViewer.Tests.csproj','-c',$Configuration,'-f',$framework,'--no-restore',"-p:CudaMajor=$CudaMajor",'--filter','FullyQualifiedName~Cuda','--logger','trx;LogFileName=cuda.trx','--results-directory',"$output/tests/$framework")
    }
    $hardware = 'not executed'
    if ($RunHardwareTests) {
        Invoke-Checked nvidia-smi @('--query-gpu=name,driver_version,driver_model.current','--format=csv')
        $matrix = Get-CudaMatrix
        $ctest = "$repo/artifacts/cuda/tools/cmake/cmake-$($matrix.cmake.version)-windows-x86_64/bin/ctest.exe"
        Invoke-Checked $ctest @('--test-dir',"$repo/build/obj/cuda/$CudaMajor/$Configuration",'--output-on-failure','--no-tests=error','--timeout','120','-L','hardware','--output-junit',"$output/hardware.xml")
        $hardware = 'passed'
    }
    $feed = "$output/feed"
    $packages = "$output/packages"
    Invoke-Checked dotnet @('pack','src/Fizzy.ImageViewer/Fizzy.ImageViewer.csproj','-c',$Configuration,'--no-restore','-o',$feed)
    Invoke-Checked dotnet @('pack','src/Fizzy.ImageViewer.Cuda/Fizzy.ImageViewer.Cuda.csproj','-c',$Configuration,'--no-restore',"-p:CudaMajor=$CudaMajor",'-o',$packages,'-p:ContinuousIntegrationBuild=true',"-p:RepositoryCommit=$($info.commit)")
    $package = "$packages/$id.$version.nupkg"
    & "$PSScriptRoot/Test-CudaPackage.ps1" -Package $package -PackageId $id -Version $version -NativeHash $info.nativeHash
    Copy-Item $package $feed
    $consumer = "$output/consumer"
    [void][IO.Directory]::CreateDirectory($consumer)
    # Prevent inheriting repository output and restore settings in this isolated consumer.
    Set-Content "$consumer/Directory.Build.props" '<Project />'
    Set-Content "$consumer/Directory.Build.targets" '<Project />'
    Set-Content "$consumer/Consumer.csproj" "<Project Sdk=`"Microsoft.NET.Sdk`"><PropertyGroup><TargetFramework>net8.0-windows</TargetFramework><UseWPF>true</UseWPF><OutputType>Exe</OutputType><RuntimeIdentifier>win-x64</RuntimeIdentifier></PropertyGroup><ItemGroup><PackageReference Include=`"$id`" Version=`"$version`" /></ItemGroup></Project>"
    Set-Content "$consumer/Program.cs" 'System.Console.WriteLine(typeof(Fizzy.ImageViewer.Cuda.ICudaFrameSource).FullName);'
    Invoke-Checked dotnet @('restore',"$consumer/Consumer.csproj",'--source',$feed,'--source','https://api.nuget.org/v3/index.json','--packages',"$output/nuget-cache")
    Invoke-Checked dotnet @('publish',"$consumer/Consumer.csproj",'-c',$Configuration,'--no-restore','--self-contained','false','-o',"$consumer/publish")
    if ((Get-Hash "$consumer/publish/Fizzy.ImageViewer.Cuda.Native.dll") -cne $info.nativeHash) { throw 'Consumer deployment contains the wrong native DLL.' }
    Copy-Item "$nativeDir/build.json" "$output/build.json"
    Write-Json @{schema=1;major=$CudaMajor;packageId=$id;version=$version;commit=$info.commit;release=[bool]$Release;passed=$true;nativeHash=$info.nativeHash;packageHash=(Get-Hash $package);nativeCompilation='passed';managedTests='passed';packageValidation='passed';hardware=$hardware;physicalPresentation='not executed'} "$output/validation.json"
    Write-Host "PASS: CUDA $CudaMajor build, managed tests and package. Hardware: $hardware."
} finally { Pop-Location; Stop-Transcript | Out-Null }

