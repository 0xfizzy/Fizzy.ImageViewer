[CmdletBinding()]
param([Parameter(Mandatory)][string]$Tag, [string]$Output = 'artifacts/bundle')
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Release.Common.psm1" -Force
Import-Module "$PSScriptRoot/Release.Remote.psm1" -Force
$downloaded = Get-ArchivedBundle $Tag
$target = Reset-Output (Resolve-FullPath $Output)
Copy-Item "$downloaded/*" $target -Recurse
$manifest = Assert-Bundle $target
$site = Reset-Output (Join-Path (Get-Repo) 'artifacts/restored-site')
Expand-SafeZip (Join-Path $target 'documentation.zip') $site
$report = Get-Content (Join-Path $target 'validation.json') -Raw | ConvertFrom-Json -AsHashtable
Assert-Tree $report.siteFiles $site
$identity = Get-Content (Join-Path $site 'identity.json') -Raw | ConvertFrom-Json
if ($identity.version -cne $manifest.version -or $identity.commit -cne $manifest.commit -or $identity.channel -cne 'Release') { throw 'Restored site identity mismatch.' }
Write-Host "PASS: $Tag restored without rebuilding. Continue the release workflow to verify NuGet and deploy the archive."
