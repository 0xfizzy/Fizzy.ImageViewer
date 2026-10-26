# Offline failure fixtures, never release artifacts. No GPU is loaded.
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Cuda.Common.psm1" -Force
$work = Reset-Output (Join-Path (Get-Repo) 'artifacts/cuda-packaging-fixture')
function Expect-Failure([scriptblock]$Action, [string]$Pattern) {
    try { & $Action } catch { if ($_.Exception.Message -notmatch $Pattern) { throw }; return }
    throw "Expected failure matching: $Pattern"
}
$native = "$work/native"
[void][IO.Directory]::CreateDirectory($native)
Expect-Failure { Assert-CudaNative 11 Release $native } 'required'
Set-Content "$native/Fizzy.ImageViewer.Cuda.Native.dll" 'fixture: not executable'
Expect-Failure { Assert-CudaNative 11 Release $native } 'required'
$record = @{schema=1;major=11;configuration='Release';commit=(Get-Commit);dirty=$true;sourceHash=(Get-CudaSourceIdentity);nativeHash=(Get-Hash "$native/Fizzy.ImageViewer.Cuda.Native.dll")}
Write-Json $record "$native/build.json"
$null = Assert-CudaNative 11 Release $native
Expect-Failure { Assert-CudaNative 12 Release $native } 'variant/configuration'
Expect-Failure { Assert-CudaNative 11 Debug $native } 'variant/configuration'
$record.sourceHash = 'wrong'
Write-Json $record "$native/build.json"
Expect-Failure { Assert-CudaNative 11 Release $native } 'source identity'
$record.sourceHash = Get-CudaSourceIdentity
Write-Json $record "$native/build.json"
Add-Content "$native/Fizzy.ImageViewer.Cuda.Native.dll" 'tampered'
Expect-Failure { Assert-CudaNative 11 Release $native } 'checksum'
foreach ($directory in @("$work/missing",$native)) {
    $output = & dotnet pack "$PSScriptRoot/../src/Fizzy.ImageViewer.Cuda/Fizzy.ImageViewer.Cuda.csproj" -c Release --no-build --no-restore "-p:CudaNativeDirectory=$directory" -o "$work/rejected-packages" 2>&1
    $exit = $LASTEXITCODE
    $output | Set-Content "$work/pack-$(Split-Path $directory -Leaf).log"
    if (!$exit -or ($output -join "`n") -notmatch 'CUDA native (DLL and build record are required|checksum mismatch)') { throw "Pack did not enforce native provenance: $output" }
}

# Synthetic packages exercise NuGet's actual transitive target import and MSBuild.
$feed = "$work/feed"
[void][IO.Directory]::CreateDirectory($feed)
foreach ($id in @('Fizzy.ImageViewer.Cuda','Fizzy.ImageViewer.Cuda11','Fizzy.ImageViewer.Cuda12','Fixture.Transitive')) {
    $dir = "$work/$id"
    [void][IO.Directory]::CreateDirectory($dir)
    $dependency = if ($id -eq 'Fixture.Transitive') { '<dependencies><group targetFramework="net8.0"><dependency id="Fizzy.ImageViewer.Cuda12" version="[1.0.0]" /></group></dependencies>' } else { '' }
    Set-Content "$dir/$id.nuspec" "<?xml version=`"1.0`"?><package><metadata><id>$id</id><version>1.0.0</version><authors>fixture</authors><description>Offline test only</description>$dependency</metadata></package>"
    if ($id -match 'Cuda(11|12)$') {
        [void][IO.Directory]::CreateDirectory("$dir/buildTransitive")
        Copy-Item "$PSScriptRoot/../src/Fizzy.ImageViewer.Cuda/buildTransitive/CudaVariant.targets" "$dir/buildTransitive/$id.targets"
    }
    [IO.Compression.ZipFile]::CreateFromDirectory($dir,"$feed/$id.1.0.0.nupkg")
}
foreach ($case in @('single','direct','transitive','legacy')) {
    $dir = "$work/$case"
    [void][IO.Directory]::CreateDirectory($dir)
    Set-Content "$dir/Directory.Build.props" '<Project />'
    Set-Content "$dir/Directory.Build.targets" '<Project />'
    $ids = @('Fizzy.ImageViewer.Cuda11')
    if ($case -eq 'direct') { $ids += 'Fizzy.ImageViewer.Cuda12' }
    if ($case -eq 'transitive') { $ids += 'Fixture.Transitive' }
    if ($case -eq 'legacy') { $ids += 'Fizzy.ImageViewer.Cuda' }
    $refs = ($ids | ForEach-Object { "<PackageReference Include=`"$_`" Version=`"1.0.0`" />" }) -join ''
    Set-Content "$dir/Test.csproj" "<Project Sdk=`"Microsoft.NET.Sdk`"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup>$refs</ItemGroup></Project>"
    Invoke-Checked dotnet @('restore',"$dir/Test.csproj",'--source',$feed,'--packages',"$work/cache")
    $text = & dotnet build "$dir/Test.csproj" --no-restore 2>&1
    $exit = $LASTEXITCODE
    $text | Set-Content "$dir/build.log"
    if ($case -eq 'single') { if ($exit) { throw "Single variant failed: $text" } }
    elseif (!$exit -or ($text -join "`n") -notmatch 'Choose exactly one CUDA package') { throw "Conflict $case was not rejected: $text" }
}
Write-Json @{passed=$true;fixture=$true;cases=@('missing DLL/record','wrong variant/configuration/source','tampered DLL','single package','direct/transitive/legacy conflicts')} "$work/result.json"
Write-Host 'PASS: CUDA native provenance failures and actual NuGet conflict checks.'
$global:LASTEXITCODE = 0

