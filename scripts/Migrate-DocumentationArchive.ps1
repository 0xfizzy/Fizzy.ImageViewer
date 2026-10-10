[CmdletBinding()]
param([Parameter(Mandatory)][string]$Archive)
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Release.Common.psm1" -Force
$Archive = Resolve-FullPath $Archive
$indexPath = Join-Path $Archive 'versions.json'
if (!(Test-Path $indexPath)) { return }
$index = Get-Content $indexPath -Raw | ConvertFrom-Json -AsHashtable
$changed = $false
foreach ($entry in $index.versions) {
    if (!$entry.ContainsKey('revisions')) { continue }
    $selected = @($entry.revisions | Where-Object { $_.revision -eq $entry.currentRevision -and $_.completed })
    if (!$entry.completed -or $selected.Count -ne 1) { throw 'Legacy version has no successful selected documentation.' }
    $root = Assert-ChildPath $Archive (Join-Path $Archive "v$($entry.version)")
    $old = Join-Path $root "r$($entry.currentRevision)"
    Assert-Tree $selected[0].files $old
    # Use an external temporary copy because replacing the version root deletes all old aliases and rN directories.
    $temp = Reset-Output (Join-Path (Get-Repo) "artifacts/docs-migration/v$($entry.version)")
    Copy-Item "$old/*" $temp -Recurse
    $identity = Get-Content (Join-Path $temp 'identity.json') -Raw | ConvertFrom-Json -AsHashtable
    $identity.directory = "v$($entry.version)"
    $identity.Remove('revision')
    Write-Json $identity (Join-Path $temp 'identity.json')
    # DocFX links inside the site are relative and keep the same depth within the version root.
    # Absolute version URLs, if present, must lose their old extra path segment.
    foreach ($file in Get-ChildItem $temp -Recurse -File | Where-Object Extension -In @('.html','.js','.json','.css','.xml')) {
        $body = [IO.File]::ReadAllText($file.FullName)
        $rewritten = [regex]::Replace($body, '/v' + [regex]::Escape($entry.version) + '/r\d+/', "/v$($entry.version)/")
        if ($rewritten -cne $body) { [IO.File]::WriteAllText($file.FullName, $rewritten) }
    }
    Copy-Item (Join-Path (Get-Repo) 'docs/site/versions.js') (Join-Path $temp 'docs/site/versions.js') -Force
    # Validate the relocated static site without changing its documentation navigation.
    & "$PSScriptRoot/Test-DocumentationSite.ps1" -Site $temp
    Remove-Item -LiteralPath $root -Recurse -Force
    Copy-Item $temp $root -Recurse
    $entry.commit = $selected[0].commit
    $entry.files = Get-Tree $root
    foreach ($key in @('revisions','currentRevision','aliases')) { $entry.Remove($key) }
    $changed = $true
}
if ($changed) { Write-Json $index $indexPath; Write-Host 'PASS: migrated legacy documentation; old revision URLs removed.' }
