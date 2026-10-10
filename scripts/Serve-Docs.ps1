[CmdletBinding()]
param([ValidateRange(1,65535)][int]$Port = 8087, [Alias('Root')][string]$SiteDirectory)
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Release.Common.psm1" -Force
if (!$SiteDirectory) { $SiteDirectory = Join-Path (Get-Repo) 'artifacts/docs/site' }
$SiteDirectory = Resolve-FullPath $SiteDirectory
$identity = Get-Content (Join-Path $SiteDirectory 'identity.json') -Raw | ConvertFrom-Json
$root = Reset-Output (Join-Path (Get-Repo) 'artifacts/docs-preview')
$mount = Assert-ChildPath $root (Join-Path $root $identity.basePath.Trim('/'))
$target = Assert-ChildPath $mount (Join-Path $mount $identity.directory)
[void][IO.Directory]::CreateDirectory((Split-Path $target -Parent))
Copy-Item $SiteDirectory $target -Recurse
$index = @{ versions = @() }
if ($identity.channel -eq 'Development') { $index.development = @{ commit = $identity.commit } }
else {
    $entry = @{ version = $identity.version; completed = $true }
    if ($identity.PSObject.Properties.Name -contains 'revision') { $entry.revisions = @(); $entry.currentRevision = $identity.revision }
    $index.versions += $entry
}
Write-Json $index (Join-Path $mount 'versions.json')
Write-Host "Preview: http://localhost:$Port$($identity.basePath)$($identity.directory)/"
Push-Location (Get-Repo)
try { Invoke-Checked dotnet @('docfx','serve',$root,'--hostname','localhost','--port',"$Port") }
finally { Pop-Location }
