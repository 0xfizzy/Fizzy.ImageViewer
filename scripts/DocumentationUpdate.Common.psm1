Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Release.Common.psm1"

function Assert-DocumentationUpdateChanges([string]$Source, [string]$Version) {
    [void](Get-Version $Version)
    $dirty = & git -C $Source status --porcelain
    if ($LASTEXITCODE -or $dirty) { throw 'Update source must be committed and clean.' }
    $changed = @(& git -C $Source diff --name-only "v$Version" HEAD)
    if ($LASTEXITCODE) { throw 'Cannot compare update source with the original release.' }
    foreach ($file in $changed) {
        if ($file -match '^(README(?:\.zh-CN)?\.md|index\.md|toc\.yml|docs/(?:zh-CN/)?[^/]+\.(md|yml)|docs/translations\.json|samples/.*\.cs)$') { continue }
        if ($file -notmatch '\.cs$') { throw "Update cannot change build/runtime configuration: $file" }
        # A /// line inside a multiline string is runtime data, not an XML comment.
        # Conservatively reject those files rather than accepting a textual false positive.
        $before = & git -C $Source show "v${Version}:$file"
        if ($LASTEXITCODE) { throw "Cannot inspect original source: $file" }
        $after = Get-Content (Join-Path $Source $file) -Raw
        if (($before -join "`n") -match '(?:"""|@")' -or $after -match '(?:"""|@")') { throw "XML-only update cannot safely classify multiline strings: $file" }
        $diff = @(& git -C $Source diff --unified=0 "v$Version" HEAD -- $file)
        if ($LASTEXITCODE) { throw "Cannot inspect $file" }
        foreach ($line in $diff) {
            if ($line -match '^(\+\+\+|---)') { continue }
            if ($line -match '^[+-]' -and $line -notmatch '^[+-]\s*(///.*)?$') { throw "Only XML comment edits are allowed in source: $file" }
        }
    }
}

Export-ModuleMember -Function Assert-DocumentationUpdateChanges
