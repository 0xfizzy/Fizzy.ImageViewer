Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Release.Common.psm1"
function Get-ReleaseRecord([string]$Tag) {
    $repository = (Get-SiteConfig).repository
    if ($env:GITHUB_REPOSITORY -and $env:GITHUB_REPOSITORY -cne $repository) { throw 'Workflow repository differs from configured release repository.' }
    $json = & gh api --paginate --slurp "repos/$repository/releases?per_page=100"
    if ($LASTEXITCODE) { throw 'Cannot enumerate GitHub releases; check GH_TOKEN permissions.' }
    $found = @($json | ConvertFrom-Json | ForEach-Object { $_ } | ForEach-Object { $_ } | Where-Object tag_name -CEQ $Tag)
    if ($found.Count -gt 1) { throw 'Multiple release records for one tag.' }
    if ($found.Count) { return $found[0] }
    return $null
}
function Assert-Bundle([string]$Bundle) {
    $manifest = Get-Content (Join-Path $Bundle 'manifest.json') -Raw | ConvertFrom-Json -AsHashtable
    if ($manifest.schema -ne 1) { throw 'Unsupported release manifest.' }
    [void](Get-Version $manifest.version)
    if ($manifest.tag -cne "v$($manifest.version)") { throw 'Invalid release tag.' }
    $actual = Get-Tree $Bundle
    $actual.Remove('manifest.json')
    if ($actual.Count -ne $manifest.files.Count) { throw 'Unexpected release files.' }
    foreach ($key in $manifest.files.Keys) { if (!$actual.Contains($key) -or $actual[$key] -cne $manifest.files[$key]) { throw "Release checksum mismatch: $key" } }
    $report = Get-Content (Join-Path $Bundle 'validation.json') -Raw | ConvertFrom-Json
    if (!$report.passed -or !$report.release -or $report.version -cne $manifest.version -or $report.commit -cne $manifest.commit) { throw 'Invalid release validation report.' }
    $repository = (Get-SiteConfig).repository
    $remoteCommit = & gh api "repos/$repository/commits/$($manifest.tag)" --jq .sha
    if ($LASTEXITCODE -or $remoteCommit -cne $manifest.commit) { throw 'Remote tag differs from archived source identity.' }
    $manifest
}
function Get-ArchivedBundle([string]$Tag) {
    if ($Tag -cnotmatch '^v(.+)$') { throw 'Expected a v-prefixed tag.' }
    [void](Get-Version $Matches[1])
    $repository = (Get-SiteConfig).repository
    $record = Get-ReleaseRecord $Tag
    if ($null -eq $record) { throw 'No release archive exists. Run the initial release validation first.' }
    $work = Reset-Output (Join-Path (Get-Repo) ("artifacts/recovery-" + [Guid]::NewGuid().ToString('N')))
    Invoke-Checked gh @('release','download',$Tag,'--repo',$repository,'--pattern','release-bundle.zip','--dir',$work)
    $bundle = Join-Path $work 'bundle'
    Expand-SafeZip "$work/release-bundle.zip" $bundle
    $manifest = Assert-Bundle $bundle
    if ($manifest.tag -cne $Tag) { throw 'Downloaded archive belongs to another tag.' }
    $bundle
}
function Test-NuGetPayload([string]$Bundle, [switch]$AllowMissing) {
    $manifest = Assert-Bundle $Bundle
    $config = Get-SiteConfig
    $id = $config.packageId.ToLowerInvariant(); $version = $manifest.version.ToLowerInvariant()
    $directory = Join-Path (Get-Repo) 'artifacts/nuget-verification'
    [void][IO.Directory]::CreateDirectory($directory)
    $download = Join-Path $directory "$id.$version.nupkg"
    $exists = Get-RemoteFile "https://api.nuget.org/v3-flatcontainer/$id/$version/$id.$version.nupkg" $download -AllowMissing:$AllowMissing
    if (!$exists) { return $false }
    $packages = @(Get-ChildItem $Bundle -Filter '*.nupkg')
    if ($packages.Count -ne 1) { throw 'Expected one archived package.' }
    Assert-PackageEqual $packages[0].FullName $download
    return $true
}
Export-ModuleMember -Function Get-ReleaseRecord, Assert-Bundle, Get-ArchivedBundle, Test-NuGetPayload
