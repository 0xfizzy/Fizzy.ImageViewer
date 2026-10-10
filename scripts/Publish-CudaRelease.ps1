[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Validate','Archive','Publish')][string]$Stage,
    [Parameter(Mandatory)][string]$Tag,
    [string]$Bundle = 'artifacts/cuda-release-bundle'
)
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Release.Remote.psm1" -Force
Import-Module "$PSScriptRoot/Release.Common.psm1" -Force
if ($Tag -cnotmatch '^v(.+)$') { throw 'Expected a version tag.' }
$version = $Matches[1]
[void](Get-Version $version)
$repository = (Get-SiteConfig).repository
$asset = 'cuda-release-bundle.zip'
if ($Stage -eq 'Publish') {
    $download = Reset-Output (Join-Path (Get-Repo) 'artifacts/cuda-release-download')
    Invoke-Checked gh @('release','download',$Tag,'--repo',$repository,'--pattern',$asset,'--dir',$download)
    $Bundle = Join-Path $download 'bundle'
    Expand-SafeZip (Join-Path $download $asset) $Bundle
}
$Bundle = Resolve-FullPath $Bundle
$manifest = Get-Content (Join-Path $Bundle 'manifest.json') -Raw | ConvertFrom-Json -AsHashtable
if ($manifest.schema -ne 1 -or $manifest.packageId -cne 'Fizzy.ImageViewer.Cuda' -or $manifest.version -cne $version -or $manifest.tag -cne $Tag) { throw 'CUDA bundle identity mismatch.' }
$tree = Get-Tree $Bundle
$tree.Remove('manifest.json')
if ($tree.Count -ne $manifest.files.Count) { throw 'CUDA bundle file count mismatch.' }
foreach ($key in $tree.Keys) { if ($tree[$key] -cne $manifest.files[$key]) { throw "CUDA bundle checksum mismatch: $key" } }
$commit = & gh api "repos/$repository/commits/$Tag" --jq .sha
if ($LASTEXITCODE -or $commit -cne $manifest.commit) { throw 'CUDA source tag differs from bundle.' }
$package = Join-Path $Bundle "Fizzy.ImageViewer.Cuda.$version.nupkg"
$zip = [IO.Compression.ZipFile]::OpenRead($package)
try {
    $spec = @($zip.Entries | Where-Object FullName -like '*.nuspec')
    if ($spec.Count -ne 1) { throw 'Expected one CUDA nuspec.' }
    $reader = [IO.StreamReader]::new($spec[0].Open())
    try { [xml]$metadata = $reader.ReadToEnd() } finally { $reader.Dispose() }
    if ($metadata.package.metadata.id -cne $manifest.packageId -or $metadata.package.metadata.version -cne $version) { throw 'CUDA package metadata mismatch.' }
    $dependencies = @($metadata.package.metadata.dependencies.group.dependency | Where-Object id -eq 'Fizzy.ImageViewer')
    if ($dependencies.Count -ne 1 -or $dependencies[0].version -cne $version) { throw 'CUDA must depend on the matching core version.' }
    foreach ($name in @('lib/net8.0-windows7.0/Fizzy.ImageViewer.Cuda.dll','lib/net8.0-windows7.0/Fizzy.ImageViewer.Cuda.xml','runtimes/win-x64/native/Fizzy.ImageViewer.Cuda.Native.dll')) {
        $entry = $zip.GetEntry($name)
        if ($null -eq $entry -or $entry.Length -eq 0) { throw "Missing CUDA package entry: $name" }
    }
    $native = $zip.GetEntry('runtimes/win-x64/native/Fizzy.ImageViewer.Cuda.Native.dll').Open()
    try { $nativeHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($native)).ToLowerInvariant() } finally { $native.Dispose() }
    if ($nativeHash -cne $manifest.nativeHash) { throw 'CUDA native binary differs from validation.' }
} finally { $zip.Dispose() }
if ($Stage -eq 'Archive') {
    $record = Get-ReleaseRecord $Tag
    if ($null -eq $record) { throw 'Create the core release before attaching CUDA.' }
    if (!@($record.assets | Where-Object name -CEQ $asset).Count) {
        $upload = Reset-Output (Join-Path (Get-Repo) 'artifacts/cuda-release-upload')
        [IO.Compression.ZipFile]::CreateFromDirectory($Bundle, (Join-Path $upload $asset))
        Invoke-Checked gh @('release','upload',$Tag,(Join-Path $upload $asset),'--repo',$repository)
    }
    $verify = Reset-Output (Join-Path (Get-Repo) 'artifacts/cuda-release-archive-check')
    Invoke-Checked gh @('release','download',$Tag,'--repo',$repository,'--pattern',$asset,'--dir',$verify)
    Expand-SafeZip (Join-Path $verify $asset) (Join-Path $verify 'bundle')
    Assert-Tree (Get-Tree $Bundle) (Join-Path $verify 'bundle')
} elseif ($Stage -eq 'Publish') {
    $coreBundle = Get-ArchivedBundle $Tag
    if (!(Test-NuGetPayload $coreBundle)) { throw 'The matching core package must be published first.' }
    $uri = "https://api.nuget.org/v3-flatcontainer/fizzy.imageviewer.cuda/$version/fizzy.imageviewer.cuda.$version.nupkg"
    $remote = Join-Path $download 'published.nupkg'
    if (!(Get-RemoteFile $uri $remote -AllowMissing)) {
        if (!$env:NUGET_API_KEY) { throw 'OIDC NuGet key required.' }
        & dotnet nuget push $package --api-key $env:NUGET_API_KEY --source https://api.nuget.org/v3/index.json --timeout 120
        # A lost push response is resolved by downloading, never by skip-duplicate.
        $visible = $false
        for ($attempt = 0; $attempt -lt 60; $attempt++) {
            if (Get-RemoteFile $uri $remote -AllowMissing) { $visible = $true; break }
            Start-Sleep -Seconds 10
        }
        if (!$visible) { throw 'CUDA publication not yet visible. Resume using the archived bundle.' }
    }
    Assert-PackageEqual $package $remote
}
Write-Host "PASS: CUDA $version $Stage."
