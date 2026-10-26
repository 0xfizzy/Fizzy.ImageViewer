[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Validate','Archive','Publish')][string]$Stage,
    [Parameter(Mandatory)][string]$Tag,
    [string]$Bundle = 'artifacts/cuda-release-bundle'
)
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Release.Remote.psm1" -Force
Import-Module "$PSScriptRoot/Release.Common.psm1" -Force
Import-Module "$PSScriptRoot/Cuda.Release.psm1" -Force
if ($Tag -cnotmatch '^v(.+)$') { throw 'Expected a version tag.' }
$version = $Matches[1]
[void](Get-Version $version)
$repository = (Get-SiteConfig).repository
$asset = 'cuda-release-bundle.zip'
$download = Join-Path (Get-Repo) 'artifacts/cuda-release-download'
if ($Stage -eq 'Publish') {
    $download = Reset-Output $download
    Invoke-Checked gh @('release','download',$Tag,'--repo',$repository,'--pattern',$asset,'--dir',$download)
    $Bundle = Join-Path $download 'bundle'
    Expand-SafeZip (Join-Path $download $asset) $Bundle
}
$Bundle = Resolve-FullPath $Bundle
$manifest = Assert-CudaBundle $Bundle $Tag
$commit = & gh api "repos/$repository/commits/$Tag" --jq .sha
if ($LASTEXITCODE -or $commit -cne $manifest.commit) { throw 'CUDA source tag differs from bundle.' }
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
    foreach ($entry in (Get-CudaBundlePackages $manifest)) {
        $package = Join-Path $Bundle $entry.path
        $id = $entry.packageId.ToLowerInvariant()
        $uri = "https://api.nuget.org/v3-flatcontainer/$id/$version/$id.$version.nupkg"
        $remote = Join-Path $download "$id.published.nupkg"
        if (!(Get-RemoteFile $uri $remote -AllowMissing)) {
            if (!$env:NUGET_API_KEY) { throw 'OIDC NuGet key required.' }
            $pushOutput = & dotnet nuget push $package --api-key $env:NUGET_API_KEY --source https://api.nuget.org/v3/index.json --timeout 120 2>&1
            $pushExit = $LASTEXITCODE
            $pushOutput | ForEach-Object { Write-Host $_ }
            if ($pushExit -ne 0 -and ($pushOutput -join "`n") -match '\b(401|403|Forbidden|Unauthorized)\b') {
                throw "NuGet rejected CUDA publication credentials or package permissions for $id. Retry the original bundle after configuring Trusted Publishing."
            }
            $visible = $false
            for ($attempt = 0; $attempt -lt 60; $attempt++) {
                if (Get-RemoteFile $uri $remote -AllowMissing) { $visible = $true; break }
                Start-Sleep -Seconds 10
            }
            if (!$visible) { throw "NuGet publication of $id not yet visible. Resume using the archived bundle." }
        }
        Assert-PackageEqual $package $remote
    }
}
Write-Host "PASS: CUDA $version $Stage."
