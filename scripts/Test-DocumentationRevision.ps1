[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Release.Common.psm1" -Force
Import-Module "$PSScriptRoot/DocumentationRevision.Common.psm1" -Force
$work = Reset-Output (Join-Path (Get-Repo) 'artifacts/revision-gate')
$archive = Join-Path $work 'archive'
function Expect-Failure([scriptblock]$Action, [string]$Pattern) {
    try { & $Action } catch { if ($_.Exception.Message -notmatch $Pattern) { throw }; return }
    throw "Expected failure: $Pattern"
}
function New-Revision([int]$Revision) {
    $site = Join-Path $work "r$Revision"
    [void][IO.Directory]::CreateDirectory($site)
    Set-Content "$site/index.html" "revision $Revision"
    [void][IO.Directory]::CreateDirectory("$site/docs")
    Set-Content "$site/docs/index.html" "guide revision $Revision"
    if ($Revision -eq 0) { Set-Content "$site/docs/removed.html" 'old guide' }
    Write-Json @{ version = '1.2.0'; revision = $Revision; channel = 'Release'; directory = "v1.2.0/r$Revision"; commit = ('a' * 39 + "$Revision"); basePath = (Get-SiteConfig).basePath } "$site/identity.json"
    $site
}
$r0 = New-Revision 0
$r1 = New-Revision 1
$r2 = New-Revision 2
Expect-Failure { & "$PSScriptRoot/Merge-DocumentationArchive.ps1" -Archive $archive -Site $r1 } 'r0'
& "$PSScriptRoot/Merge-DocumentationArchive.ps1" -Archive $archive -Site $r0
Expect-Failure { & "$PSScriptRoot/Merge-DocumentationArchive.ps1" -Archive $archive -Version 1.2.0 -Revision 0 -Activate } 'unverified'
if (Test-Path "$archive/v1.2.0/index.html") { throw 'Unverified version was activated.' }
& "$PSScriptRoot/Merge-DocumentationArchive.ps1" -Archive $archive -Version 1.2.0 -Complete
foreach ($site in @($r1,$r2)) { & "$PSScriptRoot/Merge-DocumentationArchive.ps1" -Archive $archive -Site $site }
if (!(Get-Content "$archive/v1.2.0/index.html" -Raw).Contains('r0/')) { throw 'Staging changed the active revision.' }
& "$PSScriptRoot/Merge-DocumentationArchive.ps1" -Archive $archive -Version 1.2.0 -Revision 1 -Complete
& "$PSScriptRoot/Merge-DocumentationArchive.ps1" -Archive $archive -Version 1.2.0 -Revision 2 -Complete
& "$PSScriptRoot/Merge-DocumentationArchive.ps1" -Archive $archive -Version 1.2.0 -Revision 0 -Complete
& "$PSScriptRoot/Merge-DocumentationArchive.ps1" -Archive $archive -Version 1.2.0 -Revision 1 -Complete
if (!(Get-Content "$archive/v1.2.0/index.html" -Raw).Contains('r2/')) { throw 'Older retry reversed a correction.' }
if (!(Get-Content "$archive/v1.2.0/docs/index.html" -Raw).Contains('../r2/docs/index.html')) { throw 'Deep version alias was not updated.' }
if (!(Get-Content "$archive/v1.2.0/docs/removed.html" -Raw).Contains('../r2/index.html')) { throw 'Removed page alias has no fallback.' }
& "$PSScriptRoot/Merge-DocumentationArchive.ps1" -Archive $archive -Version 1.2.0 -Revision 0 -Activate
& "$PSScriptRoot/Merge-DocumentationArchive.ps1" -Archive $archive -Version 1.2.0 -Revision 2 -Complete
if (!(Get-Content "$archive/v1.2.0/index.html" -Raw).Contains('r0/')) { throw 'Retry reversed an intentional rollback.' }
Assert-Tree (Get-Tree $r0) "$archive/v1.2.0/r0"
Assert-Tree (Get-Tree $r1) "$archive/v1.2.0/r1"
Set-Content "$r1/index.html" 'conflict'
Expect-Failure { & "$PSScriptRoot/Merge-DocumentationArchive.ps1" -Archive $archive -Site $r1 } 'Content mismatch'

# Validate documentation-only changes against a real local Git history.
$source = Join-Path $work 'source'
Invoke-Checked git @('init','--quiet',$source)
Invoke-Checked git @('-C',$source,'config','user.name','fixture')
Invoke-Checked git @('-C',$source,'config','user.email','fixture@example.invalid')
Set-Content "$source/Contract.cs" "/// <summary>Original.</summary>`npublic class Contract { }"
Invoke-Checked git @('-C',$source,'add','.')
Invoke-Checked git @('-C',$source,'commit','--quiet','-m','original')
Invoke-Checked git @('-C',$source,'tag','v1.2.0')
Set-Content "$source/Contract.cs" "/// <summary>Reviewed.</summary>`npublic class Contract { }"
Invoke-Checked git @('-C',$source,'commit','--quiet','-am','comments')
Assert-DocumentationRevisionChanges $source 1.2.0
Set-Content "$source/Contract.cs" "/// <summary>Reviewed.</summary>`npublic class Changed { }"
Invoke-Checked git @('-C',$source,'commit','--quiet','-am','runtime')
Expect-Failure { Assert-DocumentationRevisionChanges $source 1.2.0 } 'Only XML'
Write-Host 'PASS: immutable revisions, delayed activation, stale retries, rollback, conflicts and XML-only source restriction.'
