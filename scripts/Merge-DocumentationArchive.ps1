[CmdletBinding()]
param([Parameter(Mandatory)][string]$Archive, [string]$Site, [switch]$Complete, [string]$Version, [switch]$OriginalRelease)
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Release.Common.psm1" -Force
$Archive = Resolve-FullPath $Archive
[void][IO.Directory]::CreateDirectory($Archive)
& "$PSScriptRoot/Migrate-DocumentationArchive.ps1" -Archive $Archive
$indexPath = Join-Path $Archive 'versions.json'
$index = if (Test-Path $indexPath) { Get-Content $indexPath -Raw | ConvertFrom-Json -AsHashtable } else { @{ versions = @() } }
foreach ($entry in $index.versions) { Assert-Tree $entry.files (Join-Path $Archive "v$($entry.version)") }
foreach ($dir in Get-ChildItem $Archive -Directory | Where-Object Name -Match '^v\d') {
    if ($dir.Name -notin @($index.versions | ForEach-Object { "v$($_.version)" })) { throw "Unindexed archive: $($dir.Name)" }
}
if ($Site) {
    $identity = Get-Content (Join-Path $Site 'identity.json') -Raw | ConvertFrom-Json
    if ($OriginalRelease -and @($index.versions | Where-Object { $_.version -ceq $identity.version -and $_.completed }).Count) {
        Write-Host 'PASS: completed version retained; original release retry cannot undo documentation updates.'
        return
    }
    $directory = if ($identity.channel -eq 'Release') { [void](Get-Version $identity.version); "v$($identity.version)" } elseif ($identity.channel -eq 'Development') { 'dev' } else { throw 'Invalid channel.' }
    if ($identity.directory -cne $directory -or $identity.basePath -cne (Get-SiteConfig).basePath) { throw 'Documentation identity/path mismatch.' }
    $target = Assert-ChildPath $Archive (Join-Path $Archive $directory)
    if ((Resolve-FullPath $Site).StartsWith($target + [IO.Path]::DirectorySeparatorChar) -or (Resolve-FullPath $Site) -eq $target) { throw 'Site cannot be inside its replacement target.' }
    if (Test-Path $target) { Remove-Item -LiteralPath $target -Recurse -Force }
    Copy-Item -LiteralPath $Site -Destination $target -Recurse
    if ($directory -eq 'dev') { $index.development = @{ commit = $identity.commit } }
    else {
        $existing = @($index.versions | Where-Object version -CEQ $identity.version)
        $completed = $existing.Count -eq 1 -and $existing[0].completed
        $index.versions = @($index.versions | Where-Object version -CNE $identity.version) + @{ version = $identity.version; commit = $identity.commit; completed = $completed; files = (Get-Tree $target) }
    }
}
if ($Complete) {
    [void](Get-Version $Version)
    $entry = @($index.versions | Where-Object version -CEQ $Version)
    if ($entry.Count -ne 1) { throw 'Cannot complete a version that is not archived.' }
    $entry[0].completed = $true
}
$index.versions = @($index.versions | Sort-Object { [System.Management.Automation.SemanticVersion]::Parse($_.version) } -Descending)
Write-Json $index $indexPath
$stable = @($index.versions | Where-Object { $_.completed -and $_.version -notmatch '-' })
[void][IO.Directory]::CreateDirectory((Join-Path $Archive 'latest'))
foreach ($destination in @(@{ file = 'index.html'; prefix = '' }, @{ file = 'latest/index.html'; prefix = '../' })) {
    $html = if ($stable.Count) {
        $url = "$($destination.prefix)v$($stable[0].version)/"
        "<!doctype html><html lang=`"en`"><meta charset=`"utf-8`"><title>Fizzy.ImageViewer</title><meta http-equiv=`"refresh`" content=`"0;url=$url`"><a href=`"$url`">Latest stable documentation</a></html>"
    } else { "<!doctype html><html lang=`"en`"><meta charset=`"utf-8`"><title>Fizzy.ImageViewer</title><p>No stable documentation has been released.</p><a href=`"$($destination.prefix)dev/`">Development documentation</a></html>" }
    Set-Content -LiteralPath (Join-Path $Archive $destination.file) -Value $html -Encoding utf8NoBOM
}
Set-Content -LiteralPath (Join-Path $Archive '.nojekyll') -Value ''
Set-Content -LiteralPath (Join-Path $Archive '.gitattributes') -Value '* -text' -Encoding ascii
Write-Host 'PASS: version directory replaced; retained versions verified.'
