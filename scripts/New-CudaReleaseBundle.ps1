[CmdletBinding()]
param([Parameter(Mandatory)][string]$Tag)
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Release.Common.psm1" -Force
if ($Tag -cnotmatch '^v(.+)$') { throw 'Expected a version tag.' }
$version = $Matches[1]
[void](Get-Version $version)
$commit = & git rev-parse "$Tag^{commit}"
if ($LASTEXITCODE) { throw 'Missing source tag.' }
$work = Reset-Output (Join-Path (Get-Repo) 'artifacts/cuda-release-preparation')
Invoke-Checked git @('archive','--format=zip',"--output=$work/source.zip",$Tag)
$source = Join-Path $work 'source'
Expand-SafeZip "$work/source.zip" $source
$bundle = Join-Path $work 'bundle'
[void][IO.Directory]::CreateDirectory($bundle)
Push-Location $source
try {
    [xml]$project = Get-Content extensions/Fizzy.ImageViewer.Cuda/Fizzy.ImageViewer.Cuda.csproj
    if ($project.Project.PropertyGroup.Version -cne $version) { throw 'CUDA project version differs from tag.' }
    & ./scripts/Build-Cuda.ps1 -Configuration Release -Architectures '75'
    Invoke-Checked dotnet @('restore','tests/Fizzy.ImageViewer.Tests/Fizzy.ImageViewer.Tests.csproj','--locked-mode')
    Invoke-Checked dotnet @('test','tests/Fizzy.ImageViewer.Tests/Fizzy.ImageViewer.Tests.csproj','-c','Release','--no-restore','--filter','FullyQualifiedName~Cuda','--logger','trx;LogFileName=cuda.trx','--results-directory',"$work/tests")
    Invoke-Checked dotnet @('pack','extensions/Fizzy.ImageViewer.Cuda/Fizzy.ImageViewer.Cuda.csproj','-c','Release','--no-restore','-o',$bundle,'-p:ContinuousIntegrationBuild=true',"-p:RepositoryCommit=$commit","-p:SourceRevisionId=$commit")
    $nativeHash = Get-Hash artifacts/cuda/Release/Fizzy.ImageViewer.Cuda.Native.dll
} finally { Pop-Location }
Write-Json @{ tag = $Tag; commit = $commit; nativeHash = $nativeHash; cuda = '11.8'; toolset = 'v142'; architectures = '75'; managedCudaTests = 'passed'; nativeTests = 'compiled, not executed'; unverified = @('real GPU','physical display','mixed DPI') } "$bundle/validation.json"
Write-Json @{ schema = 1; packageId = 'Fizzy.ImageViewer.Cuda'; version = $version; tag = $Tag; commit = $commit; nativeHash = $nativeHash; files = (Get-Tree $bundle) } "$bundle/manifest.json"
& "$PSScriptRoot/Publish-CudaRelease.ps1" -Stage Validate -Tag $Tag -Bundle $bundle
Write-Host "CUDA bundle ready: $bundle"
