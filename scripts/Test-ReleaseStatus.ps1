[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Release.Common.psm1" -Force
Import-Module "$PSScriptRoot/Release.Remote.psm1" -Force
$repository = (Get-SiteConfig).repository
$json = & gh api --paginate --slurp "repos/$repository/tags?per_page=100"
if ($LASTEXITCODE) { throw 'Cannot enumerate release tags.' }
$failures = [Collections.Generic.List[string]]::new()
foreach ($tag in ($json | ConvertFrom-Json | ForEach-Object { $_ } | ForEach-Object { $_ })) {
    if ($tag.name -cnotmatch '^v(.+)$') { continue }
    try { [void](Get-Version $Matches[1]) } catch { continue }
    try {
        $record = Get-ReleaseRecord $tag.name
        if ($null -eq $record -or $record.draft) { throw 'Release is missing or still a draft.' }
        $bundle = Get-ArchivedBundle $tag.name
        & "$PSScriptRoot/Test-PublishedRelease.ps1" -Bundle $bundle -RequireCompleted
    } catch { $failures.Add("$($tag.name): $($_.Exception.Message). Dispatch release recovery for this tag.") }
}
if ($failures.Count) { throw ($failures -join "`n") }
Write-Host 'PASS: all retained release tags are complete.'
