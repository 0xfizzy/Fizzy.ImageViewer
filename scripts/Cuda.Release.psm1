Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Release.Common.psm1"
function Assert-CudaBundle([string]$Bundle, [string]$Tag) {
    $manifest = Get-Content "$Bundle/manifest.json" -Raw | ConvertFrom-Json -AsHashtable
    if ($manifest.schema -notin @(1,2) -or $manifest.tag -cne $Tag -or "v$($manifest.version)" -cne $Tag) { throw 'CUDA bundle identity mismatch.' }
    [void](Get-Version $manifest.version)
    $tree = Get-Tree $Bundle
    $tree.Remove('manifest.json')
    if ($tree.Count -ne $manifest.files.Count) { throw 'CUDA bundle file count mismatch.' }
    foreach ($key in $tree.Keys) { if ($tree[$key] -cne $manifest.files[$key]) { throw "CUDA bundle checksum mismatch: $key" } }
    if ($manifest.schema -eq 1) {
        if ($manifest.packageId -cne 'Fizzy.ImageViewer.Cuda') { throw 'Invalid legacy CUDA bundle.' }
        $packages = @(@{packageId=$manifest.packageId;path="$($manifest.packageId).$($manifest.version).nupkg";nativeHash=$manifest.nativeHash})
    } else {
        $packages = @($manifest.packages)
        if ($packages.Count -ne 3 -or (($packages.major | Sort-Object) -join ',') -cne '11,12,13') { throw 'CUDA bundle requires exactly variants 11,12,13.' }
        foreach ($entry in $packages) {
            $id = "Fizzy.ImageViewer.Cuda$($entry.major)"
            if ($entry.packageId -cne $id -or $entry.path -cne "cuda-$($entry.major)/packages/$id.$($manifest.version).nupkg") { throw 'Invalid CUDA variant package path.' }
            $validation = Get-Content "$Bundle/cuda-$($entry.major)/validation.json" -Raw | ConvertFrom-Json
            $build = Get-Content "$Bundle/cuda-$($entry.major)/build.json" -Raw | ConvertFrom-Json
            if (!$validation.passed -or !$validation.release -or $validation.major -ne $entry.major -or $validation.packageId -cne $id -or $validation.version -cne $manifest.version -or $validation.commit -cne $manifest.commit -or $validation.nativeHash -cne $entry.nativeHash -or $validation.packageHash -cne (Get-Hash "$Bundle/$($entry.path)")) { throw 'Invalid CUDA variant validation report.' }
            if ($build.schema -ne 1 -or $build.dirty -or $build.major -ne $entry.major -or $build.configuration -cne 'Release' -or $build.commit -cne $manifest.commit -or $build.nativeHash -cne $entry.nativeHash) { throw 'Invalid CUDA build provenance.' }
        }
    }
    foreach ($entry in $packages) {
        & "$PSScriptRoot/Test-CudaPackage.ps1" -Package "$Bundle/$($entry.path)" -PackageId $entry.packageId -Version $manifest.version -NativeHash $entry.nativeHash
    }
    $manifest
}
function Get-CudaBundlePackages($Manifest) {
    if ($Manifest.schema -eq 1) { return @(@{packageId=$Manifest.packageId;path="$($Manifest.packageId).$($Manifest.version).nupkg";nativeHash=$Manifest.nativeHash}) }
    @($Manifest.packages | Sort-Object major)
}
Export-ModuleMember -Function Assert-CudaBundle, Get-CudaBundlePackages
