[CmdletBinding()]
param([Parameter(Mandatory)][string]$Archive, [string]$Site, [switch]$Complete, [string]$Version, [ValidateRange(0,2147483647)][int]$Revision = 0, [switch]$Activate)
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Release.Common.psm1" -Force
$Archive = Resolve-FullPath $Archive
if (!(Test-Path $Archive)) { [void][IO.Directory]::CreateDirectory($Archive) }
$indexPath = Join-Path $Archive 'versions.json'
$index = if (Test-Path $indexPath) { Get-Content $indexPath -Raw | ConvertFrom-Json -AsHashtable } else { @{ versions = @() } }
# Check every retained version before writing anything; unknown directories are never silently accepted.
foreach ($entry in $index.versions) {
    if ($entry.ContainsKey('revisions')) {
        foreach ($retained in $entry.revisions) { Assert-Tree $retained.files (Join-Path $Archive "v$($entry.version)/r$($retained.revision)") }
        foreach ($directory in Get-ChildItem (Join-Path $Archive "v$($entry.version)") -Directory | Where-Object Name -Match '^r\d+$') {
            if ($directory.Name -notin @($entry.revisions | ForEach-Object { "r$($_.revision)" })) { throw "Unindexed revision: $($directory.Name)" }
        }
    } else { Assert-Tree $entry.files (Join-Path $Archive "v$($entry.version)") }
}
foreach ($dir in Get-ChildItem $Archive -Directory | Where-Object Name -Match '^v\d') {
    if ($dir.Name -notin @($index.versions | ForEach-Object { "v$($_.version)" })) { throw "Unindexed archive: $($dir.Name)" }
}
if ($Site) {
    $identity = Get-Content (Join-Path $Site 'identity.json') -Raw | ConvertFrom-Json
    $isRevision = $identity.PSObject.Properties.Name -contains 'revision'
    $directory = if ($identity.channel -eq 'Release') {
        [void](Get-Version $identity.version)
        if ($isRevision) {
            if ($identity.revision -isnot [long] -and $identity.revision -isnot [int]) { throw 'Revision must be a nonnegative integer.' }
            if ($identity.revision -lt 0) { throw 'Revision must be a nonnegative integer.' }
            "v$($identity.version)/r$($identity.revision)"
        } else { "v$($identity.version)" }
    } elseif ($identity.channel -eq 'Development') { 'dev' } else { throw 'Invalid channel.' }
    if ($identity.directory -cne $directory -or $identity.basePath -cne (Get-SiteConfig).basePath) { throw 'Documentation identity/path mismatch.' }
    $target = Assert-ChildPath $Archive (Join-Path $Archive $directory)
    if ($directory -eq 'dev') {
        if (Test-Path $target) { Remove-Item -LiteralPath $target -Recurse -Force }
        Copy-Item -LiteralPath $Site -Destination $target -Recurse
        $index.development = @{ commit = $identity.commit }
    } else {
        $existing = @($index.versions | Where-Object version -CEQ $identity.version)
        if ($isRevision) {
            if ($existing.Count -gt 1) { throw 'Version identity conflict.' }
            if (!$existing.Count) {
                if ($identity.revision -ne 0) { throw 'Original r0 must be archived first.' }
                $entry = @{ version = $identity.version; commit = $identity.commit; completed = $false; currentRevision = $null; revisions = @() }
                $index.versions += $entry
            } else {
                $entry = $existing[0]
                if (!$entry.ContainsKey('revisions')) { throw 'Legacy immutable version cannot be rewritten; retain its original directory.' }
            }
            $retained = @($entry.revisions | Where-Object revision -EQ $identity.revision)
            if ($retained.Count) {
                if ($retained[0].commit -cne $identity.commit) { throw 'Revision identity conflict.' }
                Assert-Tree $retained[0].files $Site
            } else {
                if (Test-Path $target) { throw 'Refusing to overwrite an existing directory.' }
                [void][IO.Directory]::CreateDirectory((Split-Path $target -Parent))
                Copy-Item -LiteralPath $Site -Destination $target -Recurse
                $entry.revisions += @{ revision = $identity.revision; commit = $identity.commit; completed = $false; files = (Get-Tree $Site) }
            }
        } elseif ($existing.Count) {
            if ($existing.Count -ne 1 -or $existing[0].commit -cne $identity.commit) { throw 'Version identity conflict.' }
            Assert-Tree $existing[0].files $Site
        } else {
            if (Test-Path $target) { throw 'Refusing to overwrite an existing directory.' }
            Copy-Item -LiteralPath $Site -Destination $target -Recurse
            $index.versions += @{ version = $identity.version; commit = $identity.commit; completed = $false; files = (Get-Tree $Site) }
        }
    }
}
if ($Complete -or $Activate) {
    [void](Get-Version $Version)
    $entry = @($index.versions | Where-Object version -CEQ $Version)
    if ($entry.Count -ne 1) { throw 'Cannot complete a version that is not archived.' }
    if ($entry[0].ContainsKey('revisions')) {
        $selected = @($entry[0].revisions | Where-Object revision -EQ $Revision)
        if ($selected.Count -ne 1) { throw 'Cannot activate a revision that is not archived.' }
        if ($Activate -and !$selected[0].completed) { throw 'Cannot roll back to an unverified revision.' }
        $wasCompleted = $selected[0].completed
        $selected[0].completed = $true
        # A same-tag retry must not undo a later correction or an intentional rollback.
        if ($Activate -or $null -eq $entry[0].currentRevision -or ($Complete -and !$wasCompleted -and $Revision -gt $entry[0].currentRevision)) {
            $entry[0].currentRevision = $Revision
        }
        $entry[0].completed = $true
    } else {
        if ($Revision -ne 0) { throw 'Legacy version has no revision.' }
        $entry[0].completed = $true
    }
}
foreach ($entry in $index.versions | Where-Object { $_.ContainsKey('revisions') -and $_.completed }) {
    $selected = @($entry.revisions | Where-Object revision -EQ $entry.currentRevision)[0]
    $oldAliases = if ($entry.ContainsKey('aliases')) { @($entry.aliases) } else { @('index.html') }
    $aliases = @(@($oldAliases) + @($selected.files.Keys | Where-Object { $_.EndsWith('.html') }) | Sort-Object -Unique)
    $versionRoot = Join-Path $Archive "v$($entry.version)"
    foreach ($relative in $aliases) {
        $path = Assert-ChildPath $versionRoot (Join-Path $versionRoot $relative)
        if ((Test-Path $path) -and $relative -notin $oldAliases) { throw "Refusing to replace unindexed alias: $relative" }
        $targetPage = if ($selected.files.Contains($relative)) { $relative } elseif ($relative.StartsWith('docs/zh-CN/')) { 'docs/zh-CN/index.html' } else { 'index.html' }
        $url = [IO.Path]::GetRelativePath((Split-Path $path -Parent), (Join-Path $versionRoot "r$($entry.currentRevision)/$targetPage")).Replace('\','/')
        $escaped = [Net.WebUtility]::HtmlEncode($url)
        [void][IO.Directory]::CreateDirectory((Split-Path $path -Parent))
        Set-Content -LiteralPath $path -Encoding utf8NoBOM -Value "<!doctype html><html lang=`"en`"><meta charset=`"utf-8`"><title>Documentation</title><meta http-equiv=`"refresh`" content=`"0;url=$escaped`"><a href=`"$escaped`">Current documentation revision</a></html>"
    }
    $entry.aliases = $aliases
}
$index.versions = @($index.versions | Sort-Object { [System.Management.Automation.SemanticVersion]::Parse($_.version) } -Descending)
Write-Json $index $indexPath
$stable = @($index.versions | Where-Object { $_.completed -and $_.version -notmatch '-' })
[void][IO.Directory]::CreateDirectory((Join-Path $Archive 'latest'))
foreach ($destination in @(@{ file = 'index.html'; prefix = '' }, @{ file = 'latest/index.html'; prefix = '../' })) {
    $html = if ($stable.Count) {
        $url = "$($destination.prefix)v$($stable[0].version)/"
        "<!doctype html><html lang=`"en`"><meta charset=`"utf-8`"><title>Fizzy.ImageViewer</title><meta http-equiv=`"refresh`" content=`"0;url=$url`"><a href=`"$url`">Latest stable documentation</a></html>"
    } else {
        "<!doctype html><html lang=`"en`"><meta charset=`"utf-8`"><title>Fizzy.ImageViewer</title><p>No stable documentation has been released.</p><a href=`"$($destination.prefix)dev/`">Development documentation</a></html>"
    }
    Set-Content -LiteralPath (Join-Path $Archive $destination.file) -Value $html -Encoding utf8NoBOM
}
Set-Content -LiteralPath (Join-Path $Archive '.nojekyll') -Value ''
# The archive is byte-addressed. Git's Windows newline conversion must never alter retained files.
Set-Content -LiteralPath (Join-Path $Archive '.gitattributes') -Value '* -text' -Encoding ascii
Write-Host 'PASS: archive merged; retained versions verified.'
