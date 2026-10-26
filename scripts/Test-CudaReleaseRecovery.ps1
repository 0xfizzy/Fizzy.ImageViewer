# Execute in a separate PowerShell process. All remote boundaries are fake.
$ErrorActionPreference='Stop'
Import-Module "$PSScriptRoot/Release.Common.psm1" -Force
Import-Module "$PSScriptRoot/Cuda.Release.psm1" -Force
$work=Reset-Output (Join-Path (Get-Repo) 'artifacts/cuda-recovery-fixture')
$bundle="$work/bundle"
[void][IO.Directory]::CreateDirectory($bundle)
$commit='a'*40
$version='1.3.0'
function New-FixturePackage($Id,$Path) {
    $payload="$work/payload-$Id"
    [void][IO.Directory]::CreateDirectory("$payload/lib/net8.0-windows7.0")
    [void][IO.Directory]::CreateDirectory("$payload/runtimes/win-x64/native")
    Set-Content "$payload/lib/net8.0-windows7.0/Fizzy.ImageViewer.Cuda.dll" 'fixture managed'
    Set-Content "$payload/lib/net8.0-windows7.0/Fizzy.ImageViewer.Cuda.xml" '<doc />'
    Set-Content "$payload/runtimes/win-x64/native/Fizzy.ImageViewer.Cuda.Native.dll" "fixture native $Id"
    Set-Content "$payload/$Id.nuspec" "<package><metadata><id>$Id</id><version>$version</version><dependencies><group><dependency id=`"Fizzy.ImageViewer`" version=`"$version`" /></group></dependencies></metadata></package>"
    if($Id -ne 'Fizzy.ImageViewer.Cuda'){
        [void][IO.Directory]::CreateDirectory("$payload/buildTransitive")
        Set-Content "$payload/buildTransitive/$Id.targets" '<Project />'
    }
    [void][IO.Directory]::CreateDirectory((Split-Path $Path -Parent))
    [IO.Compression.ZipFile]::CreateFromDirectory($payload,$Path)
    Get-Hash "$payload/runtimes/win-x64/native/Fizzy.ImageViewer.Cuda.Native.dll"
}
$entries=@()
$matrix = Get-Content "$PSScriptRoot/../eng/cuda-matrix.json" -Raw | ConvertFrom-Json
foreach($major in @(11,12,13)){
    $id="Fizzy.ImageViewer.Cuda$major"
    $path="cuda-$major/packages/$id.$version.nupkg"
    $hash=New-FixturePackage $id "$bundle/$path"
    $variant = $matrix.variants | Where-Object major -eq $major
    Write-Json @{schema=1;major=$major;configuration='Release';commit=$commit;dirty=$false;nativeHash=$hash;nvcc=$variant.nvcc;toolkit=$variant.toolkit;architectures=$variant.architectures;msvc="$($matrix.msvc).33130";sdk=$matrix.sdk;cmake=$matrix.cmake.version;ninja=$matrix.ninja.version} "$bundle/cuda-$major/build.json"
    Write-Json @{passed=$true;release=$true;major=$major;packageId=$id;version=$version;commit=$commit;nativeHash=$hash;packageHash=(Get-Hash "$bundle/$path")} "$bundle/cuda-$major/validation.json"
    $entries+=@{major=$major;packageId=$id;path=$path;nativeHash=$hash}
}
$manifest=@{schema=2;tag="v$version";version=$version;commit=$commit;packages=$entries;files=(Get-Tree $bundle)}
Write-Json $manifest "$bundle/manifest.json"
$null=Assert-CudaBundle $bundle "v$version"
function Expect-Failure([scriptblock]$Action,[string]$Pattern){
    try{& $Action}catch{if($_.Exception.Message -notmatch $Pattern){throw};return}
    throw "Expected failure: $Pattern"
}
# Exercise aggregation without creating or moving a real source tag.
function global:git {
    # New-CudaReleaseBundle uses: git -C <repo> rev-parse refs/tags/<tag>^{commit}
    if ($args.Count -ne 4 -or $args[2] -ne 'rev-parse' -or $args[3] -ne 'refs/tags/v1.3.0^{commit}') { throw 'Unexpected Git call in aggregation fixture.' }
    $global:LASTEXITCODE = 0
    'a' * 40
}
try {
    & "$PSScriptRoot/New-CudaReleaseBundle.ps1" -Tag "v$version" -InputDirectory $bundle -Output "$work/aggregated"
    $validationPath = "$bundle/cuda-13/validation.json"
    $validation = Get-Content $validationPath -Raw
    Remove-Item -LiteralPath $validationPath
    Expect-Failure { & "$PSScriptRoot/New-CudaReleaseBundle.ps1" -Tag "v$version" -InputDirectory $bundle -Output "$work/incomplete" } 'Missing validated CUDA 13'
    if (Test-Path "$work/incomplete/manifest.json") { throw 'Incomplete matrix produced a sealed manifest.' }
    Set-Content $validationPath $validation -NoNewline
    $buildPath = "$bundle/cuda-11/build.json"
    $buildText = Get-Content $buildPath -Raw
    $wrongBuild = $buildText | ConvertFrom-Json
    $wrongBuild.toolkit = 'wrong'
    Write-Json $wrongBuild $buildPath
    Expect-Failure { & "$PSScriptRoot/New-CudaReleaseBundle.ps1" -Tag "v$version" -InputDirectory $bundle -Output "$work/wrong-toolchain" } 'toolchain differs'
    Set-Content $buildPath $buildText -NoNewline
} finally { Remove-Item Function:global:git }
$manifest.packages=@($entries[0],$entries[1])
Write-Json $manifest "$bundle/manifest.json"
Expect-Failure {Assert-CudaBundle $bundle "v$version"} 'exactly variants'
$manifest.packages=$entries
Write-Json $manifest "$bundle/manifest.json"
Add-Content "$bundle/cuda-11/build.json" 'tamper'
Expect-Failure {Assert-CudaBundle $bundle "v$version"} 'checksum'
Write-Json @{schema=1;major=11;configuration='Release';commit=$commit;dirty=$false;nativeHash=$entries[0].nativeHash} "$bundle/cuda-11/build.json"
$manifest.files=Get-Tree $bundle
$manifest.files.Remove('manifest.json')
Write-Json $manifest "$bundle/manifest.json"

# Minimal original core archive for the existing release validation protocol.
$core="$work/core"
[void][IO.Directory]::CreateDirectory($core)
Copy-Item "$bundle/$($entries[0].path)" "$core/Fizzy.ImageViewer.$version.nupkg"
Write-Json @{passed=$true;release=$true;version=$version;commit=$commit} "$core/validation.json"
Write-Json @{schema=1;tag="v$version";version=$version;commit=$commit;files=(Get-Tree $core)} "$core/manifest.json"
[IO.Compression.ZipFile]::CreateFromDirectory($core,"$work/core.zip")
$global:CudaFixture=@{work=$work;commit=$commit;assets=@(@{name='release-bundle.zip'});published=@{};pushes=@{};deny='Fizzy.ImageViewer.Cuda12'}
$global:CudaFixture.published["fizzy.imageviewer.$version.nupkg"]="$core/Fizzy.ImageViewer.$version.nupkg"
function global:gh {
    $a=@($args);$global:LASTEXITCODE=0
    if($a[0] -eq 'api'){
        if(($a -join ' ') -match '/commits/'){ $global:CudaFixture.commit;return }
        if(($a -join ' ') -match '/releases\?'){
            ConvertTo-Json -InputObject @(@(@{tag_name='v1.3.0';draft=$false;assets=$global:CudaFixture.assets})) -Depth 8 -Compress
            return
        }
    }
    if($a[0] -eq 'release' -and $a[1] -eq 'upload'){
        Copy-Item $a[3] "$($global:CudaFixture.work)/remote-cuda.zip"
        $global:CudaFixture.assets+=@{name='cuda-release-bundle.zip'}
        return
    }
    if($a[0] -eq 'release' -and $a[1] -eq 'download'){
        $pattern=$a[[Array]::IndexOf($a,'--pattern')+1]
        $dest=$a[[Array]::IndexOf($a,'--dir')+1]
        $source=if($pattern -eq 'release-bundle.zip'){'core.zip'}else{'remote-cuda.zip'}
        Copy-Item "$($global:CudaFixture.work)/$source" "$dest/$pattern"
        return
    }
    throw "Unexpected fake gh: $a"
}
function global:dotnet {
    if($args[0] -ne 'nuget' -or $args[1] -ne 'push'){throw 'Recovery must never build.'}
    $name=Split-Path $args[2] -Leaf
    $id=$name -replace '\.1\.3\.0\.nupkg$',''
    if($id -eq $global:CudaFixture.deny){$global:LASTEXITCODE=1;'403 Forbidden';return}
    if(!$global:CudaFixture.pushes.ContainsKey($id)){$global:CudaFixture.pushes[$id]=0}
    $global:CudaFixture.pushes[$id]++
    $dest="$($global:CudaFixture.work)/remote-$name"
    Copy-Item $args[2] $dest
    $global:CudaFixture.published[$name.ToLowerInvariant()]=$dest
    $global:LASTEXITCODE=1 # Server accepted bytes; push response was lost.
}
function global:Invoke-WebRequest {
    param($Uri,$OutFile,$TimeoutSec)
    $name=([uri]$Uri).Segments[-1].ToLowerInvariant()
    if(!$global:CudaFixture.published.ContainsKey($name)){
        $e=[Net.Http.HttpRequestException]::new('Not found')
        $e | Add-Member -NotePropertyName Response -NotePropertyValue ([pscustomobject]@{StatusCode=404})
        throw $e
    }
    Copy-Item $global:CudaFixture.published[$name] $OutFile
}
$oldKey=$env:NUGET_API_KEY
try{
    $env:NUGET_API_KEY='fixture-only'
    & "$PSScriptRoot/Publish-CudaRelease.ps1" -Stage Archive -Tag "v$version" -Bundle $bundle
    Expect-Failure {& "$PSScriptRoot/Publish-CudaRelease.ps1" -Stage Publish -Tag "v$version"} 'credentials or package permissions'
    if($global:CudaFixture.pushes['Fizzy.ImageViewer.Cuda11'] -ne 1){throw 'First variant did not publish exactly once.'}
    $global:CudaFixture.deny=''
    & "$PSScriptRoot/Publish-CudaRelease.ps1" -Stage Publish -Tag "v$version"
    & "$PSScriptRoot/Publish-CudaRelease.ps1" -Stage Publish -Tag "v$version"
    foreach($id in @('Fizzy.ImageViewer.Cuda11','Fizzy.ImageViewer.Cuda12','Fizzy.ImageViewer.Cuda13')){
        if($global:CudaFixture.pushes[$id] -ne 1){throw 'Recovery republished a package.'}
    }
    $global:CudaFixture.commit='b'*40
    Expect-Failure {& "$PSScriptRoot/Publish-CudaRelease.ps1" -Stage Publish -Tag "v$version"} 'source tag differs'
    $global:CudaFixture.commit=$commit
    # Different, valid package bytes at an already published version must stop recovery.
    $global:CudaFixture.published["fizzy.imageviewer.cuda11.$version.nupkg"]=$global:CudaFixture.published["fizzy.imageviewer.cuda12.$version.nupkg"]
    Expect-Failure {& "$PSScriptRoot/Publish-CudaRelease.ps1" -Stage Publish -Tag "v$version"} 'Published package'

    $legacy="$work/legacy"
    [void][IO.Directory]::CreateDirectory($legacy)
    $legacyHash=New-FixturePackage 'Fizzy.ImageViewer.Cuda' "$legacy/Fizzy.ImageViewer.Cuda.$version.nupkg"
    Write-Json @{schema=1;packageId='Fizzy.ImageViewer.Cuda';tag="v$version";version=$version;commit=$commit;nativeHash=$legacyHash;files=(Get-Tree $legacy)} "$legacy/manifest.json"
    $null=Assert-CudaBundle $legacy "v$version"
    # Preserve the legacy schema and original bytes when publishing/restoring it.
    Remove-Item -LiteralPath "$work/remote-cuda.zip"
    [IO.Compression.ZipFile]::CreateFromDirectory($legacy,"$work/remote-cuda.zip")
    & "$PSScriptRoot/Publish-CudaRelease.ps1" -Stage Publish -Tag "v$version"
    & "$PSScriptRoot/Publish-CudaRelease.ps1" -Stage Publish -Tag "v$version"
    if($global:CudaFixture.pushes['Fizzy.ImageViewer.Cuda'] -ne 1){throw 'Legacy recovery republished.'}
    Write-Json @{passed=$true;simulation=$true;scenarios=@('missing variant','tampered archive','partial publication','lost push response','repeat without rebuild','tag conflict','published payload conflict','legacy recovery')} "$work/result.json"
    Write-Host 'PASS: offline CUDA publication and recovery failures; no real publication.'
}finally{
    $env:NUGET_API_KEY=$oldKey
    Remove-Item Function:global:gh,Function:global:dotnet,Function:global:Invoke-WebRequest
    Remove-Variable CudaFixture -Scope Global
}
