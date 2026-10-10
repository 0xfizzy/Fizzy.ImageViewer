Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Release.Common.psm1"

function Assert-DocumentationRevisionChanges([string]$Source, [string]$Version) {
    [void](Get-Version $Version)
    $dirty = & git -C $Source status --porcelain
    if ($LASTEXITCODE -or $dirty) { throw 'Revision source must be committed and clean.' }
    $changed = @(& git -C $Source diff --name-only "v$Version" HEAD)
    if ($LASTEXITCODE) { throw 'Cannot compare revision source with the original release.' }
    foreach ($file in $changed) {
        if ($file -match '^(README(?:\.zh-CN)?\.md|index\.md|toc\.yml|docs/(?:zh-CN/)?[^/]+\.(md|yml)|docs/translations\.json|samples/.*\.cs)$') { continue }
        if ($file -notmatch '\.cs$') { throw "Revision cannot change build/runtime configuration: $file" }
        # A /// line inside a multiline string is runtime data, not an XML comment.
        # Conservatively reject those files rather than accepting a textual false positive.
        $before = & git -C $Source show "v${Version}:$file"
        if ($LASTEXITCODE) { throw "Cannot inspect original source: $file" }
        $after = Get-Content (Join-Path $Source $file) -Raw
        if (($before -join "`n") -match '(?:"""|@")' -or $after -match '(?:"""|@")') { throw "XML-only revision cannot safely classify multiline strings: $file" }
        $diff = @(& git -C $Source diff --unified=0 "v$Version" HEAD -- $file)
        if ($LASTEXITCODE) { throw "Cannot inspect $file" }
        foreach ($line in $diff) {
            if ($line -match '^(\+\+\+|---)') { continue }
            if ($line -match '^[+-]' -and $line -notmatch '^[+-]\s*(///.*)?$') { throw "Only XML comment edits are allowed in source: $file" }
        }
    }
}

function Assert-RevisionBundle([string]$Bundle, [string]$Version, [int]$Revision) {
    $manifest = Get-Content (Join-Path $Bundle 'revision.json') -Raw | ConvertFrom-Json -AsHashtable
    if ($manifest.schema -ne 1 -or $manifest.version -cne $Version -or $manifest.revision -ne $Revision) { throw 'Revision bundle identity mismatch.' }
    $site = Join-Path $Bundle 'site'
    Assert-Tree $manifest.files $site
    $identity = Get-Content (Join-Path $site 'identity.json') -Raw | ConvertFrom-Json
    if ($identity.directory -cne "v$Version/r$Revision" -or $identity.commit -cne $manifest.commit -or $identity.channel -cne 'Release') { throw 'Revision site identity mismatch.' }
    $manifest
}

function Test-OnlineRevision([string]$Archive, [string]$Version, [int]$Revision, [switch]$RequireActive) {
    $config = Get-SiteConfig
    $index = Get-Content (Join-Path $Archive 'versions.json') -Raw | ConvertFrom-Json
    $versionEntry = @($index.versions | Where-Object version -CEQ $Version)
    if ($versionEntry.Count -ne 1) { throw 'Unknown version.' }
    $entry = @($versionEntry[0].revisions | Where-Object revision -EQ $Revision)
    if ($entry.Count -ne 1) { throw 'Unknown revision.' }
    $work = Reset-Output (Join-Path (Get-Repo) 'artifacts/revision-online')
    foreach ($file in $entry[0].files.PSObject.Properties) {
        $target = Assert-ChildPath $work (Join-Path $work $file.Name)
        [void][IO.Directory]::CreateDirectory((Split-Path $target -Parent))
        $null = Get-RemoteFile "$($config.origin)$($config.basePath)v$Version/r$Revision/$($file.Name)" $target
        if ((Get-Hash $target) -cne $file.Value) { throw "Online revision mismatch: $($file.Name)" }
    }
    if ($RequireActive) {
        foreach ($file in @('versions.json', "v$Version/index.html", 'index.html', 'latest/index.html')) {
            $target = Join-Path $work ($file.Replace('/','-'))
            $null = Get-RemoteFile "$($config.origin)$($config.basePath)$file" $target
            if ((Get-Hash $target) -cne (Get-Hash (Join-Path $Archive $file))) { throw "Online activation mismatch: $file" }
        }
    }
}
Export-ModuleMember -Function Assert-DocumentationRevisionChanges, Assert-RevisionBundle, Test-OnlineRevision
