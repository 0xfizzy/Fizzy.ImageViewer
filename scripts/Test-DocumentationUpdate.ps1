[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Release.Common.psm1" -Force
Import-Module "$PSScriptRoot/DocumentationUpdate.Common.psm1" -Force
$work = Reset-Output (Join-Path (Get-Repo) 'artifacts/update-gate')
$archive = Join-Path $work 'archive'
function Expect-Failure([scriptblock]$Action, [string]$Pattern) {
 try { & $Action } catch { if ($_.Exception.Message -notmatch $Pattern) { throw }; return }
 throw "Expected failure: $Pattern"
}
function New-Site([string]$Name,[string]$Version) {
 $site=Join-Path $work $Name
 [void][IO.Directory]::CreateDirectory($site)
 Set-Content "$site/index.html" "content $Name"
 Write-Json @{version=$Version;channel='Release';directory="v$Version";commit=('a'*40);basePath=(Get-SiteConfig).basePath} "$site/identity.json"
 $site
}
$old=New-Site old 1.2.0
Set-Content "$old/removed.html" 'obsolete'
$new=New-Site new 1.2.0
$other=New-Site other 1.3.0
& "$PSScriptRoot/Merge-DocumentationArchive.ps1" -Archive $archive -Site $old -Complete -Version 1.2.0
& "$PSScriptRoot/Merge-DocumentationArchive.ps1" -Archive $archive -Site $other -Complete -Version 1.3.0
& "$PSScriptRoot/Merge-DocumentationArchive.ps1" -Archive $archive -Site $new
if(Test-Path "$archive/v1.2.0/removed.html"){throw 'Deleted page survived overwrite.'}
Assert-Tree (Get-Tree $new) "$archive/v1.2.0"
Assert-Tree (Get-Tree $other) "$archive/v1.3.0"
if(!(Get-Content "$archive/latest/index.html" -Raw).Contains('v1.3.0/')){throw 'Updating older version changed latest.'}
& "$PSScriptRoot/Merge-DocumentationArchive.ps1" -Archive $archive -Site $old -OriginalRelease
Assert-Tree (Get-Tree $new) "$archive/v1.2.0"
Invoke-Checked git @('init','--quiet',$archive)
Invoke-Checked git @('-C',$archive,'config','user.name','fixture')
Invoke-Checked git @('-C',$archive,'config','user.email','fixture@example.invalid')
Invoke-Checked git @('-C',$archive,'add','.')
Invoke-Checked git @('-C',$archive,'commit','--quiet','-m','baseline')
$baseline=& git -C $archive rev-parse HEAD
& "$PSScriptRoot/Publish-Documentation.ps1" -Action Success -Archive $archive -RunId 1
Invoke-Checked git @('-C',$archive,'add','.')
Invoke-Checked git @('-C',$archive,'commit','--quiet','-m','successful')
$head=& git -C $archive rev-parse HEAD
Expect-Failure { & "$PSScriptRoot/Publish-Documentation.ps1" -Action Stage -Archive $archive -RunId 2 -ExpectedArchive ('b'*40) } 'Stale archive'
Expect-Failure { & "$PSScriptRoot/Publish-Documentation.ps1" -Action Stage -Archive $archive -RunId 2 -ExpectedArchive $head -Tag v1.2.0 -ArchiveCommit ('b'*40) } 'successful deployed'
& "$PSScriptRoot/Publish-Documentation.ps1" -Action Stage -Archive $archive -RunId 2 -ExpectedArchive $head -Tag v1.2.0 -ArchiveCommit $baseline
if(!(Test-Path "$archive/.publication/runs/2/bundle/site/index.html")){throw 'Candidate not durably staged.'}
& "$PSScriptRoot/Publish-Documentation.ps1" -Action Stage -Archive $archive -RunId 3 -ResumeRunId 2
Assert-Tree (Get-Tree "$archive/.publication/runs/2/bundle") "$archive/.publication/runs/3/bundle"
# A later successful deployment makes the old candidate stale.
Invoke-Checked git @('-C',$archive,'add','.')
Invoke-Checked git @('-C',$archive,'commit','--quiet','-m','candidate')
& "$PSScriptRoot/Publish-Documentation.ps1" -Action Success -Archive $archive -RunId 3
Expect-Failure { & "$PSScriptRoot/Publish-Documentation.ps1" -Action Stage -Archive $archive -RunId 4 -ResumeRunId 2 } 'Stale candidate'
# Migrate legacy selected content without aliases or old directories.
$legacy=Join-Path $work 'legacy'
[void][IO.Directory]::CreateDirectory("$legacy/v1.2.0/r0/docs/site")
Copy-Item "$PSScriptRoot/../docs/site/versions.js" "$legacy/v1.2.0/r0/docs/site/versions.js"
Set-Content "$legacy/v1.2.0/r0/index.html" '<html>legacy</html>'
Write-Json @{version='1.2.0';channel='Release';directory='v1.2.0/r0';revision=0;commit=('a'*40);basePath=(Get-SiteConfig).basePath} "$legacy/v1.2.0/r0/identity.json"
Write-Json @{versions=@(@{version='1.2.0';commit=('a'*40);completed=$true;currentRevision=0;revisions=@(@{revision=0;commit=('a'*40);completed=$true;files=(Get-Tree "$legacy/v1.2.0/r0")})})} "$legacy/versions.json"
& "$PSScriptRoot/Merge-DocumentationArchive.ps1" -Archive $legacy
if(Test-Path "$legacy/v1.2.0/r0"){throw 'Legacy URL remains.'}
$identity=Get-Content "$legacy/v1.2.0/identity.json" -Raw | ConvertFrom-Json
if($identity.directory -cne 'v1.2.0' -or $identity.PSObject.Properties.Name -contains 'revision'){throw 'Migration identity is incorrect.'}
if((Get-Content "$legacy/v1.2.0/index.html" -Raw).Contains('/r0/')){throw 'Migration retained absolute revision link.'}
& "$PSScriptRoot/Merge-DocumentationArchive.ps1" -Archive $legacy

# Validate documentation-only changes against a real local Git history.
$source = Join-Path $work 'source'
Invoke-Checked git @('init','--quiet',$source)
Invoke-Checked git @('-C',$source,'config','user.name','fixture')
Invoke-Checked git @('-C',$source,'config','user.email','fixture@example.invalid')
Set-Content "$source/Contract.cs" "/// <summary>Original.</summary>`npublic class Contract { }"
Invoke-Checked git @('-C',$source,'add','.')
Invoke-Checked git @('-C',$source,'commit','--quiet','-m','original')
Invoke-Checked git @('-C',$source,'tag','v1.2.0')
[void][IO.Directory]::CreateDirectory("$source/docs/en")
Set-Content "$source/docs/en/index.md" "# Documentation"
Set-Content "$source/docs/en/toc.yml" "- name: English`n  href: index.md"
Invoke-Checked git @('-C',$source,'add','docs/en')
Set-Content "$source/Contract.cs" "/// <summary>Reviewed.</summary>`npublic class Contract { }"
Invoke-Checked git @('-C',$source,'commit','--quiet','-am','comments')
Assert-DocumentationUpdateChanges $source 1.2.0
# The new guide layout and bilingual entry are documentation; tooling is not.
[void][IO.Directory]::CreateDirectory("$source/docs/zh-CN")
Set-Content "$source/docs/index.md" '# Bilingual entry'
Set-Content "$source/docs/toc.yml" "- name: English`n  href: en/index.md"
Set-Content "$source/docs/zh-CN/index.md" '# 中文'
Invoke-Checked git @('-C',$source,'add','docs')
Invoke-Checked git @('-C',$source,'commit','--quiet','-m','language guides')
Assert-DocumentationUpdateChanges $source 1.2.0
foreach ($forbidden in @('docs/docfx.json','docs/en/settings.yml','docs/template/custom.md','scripts/change.ps1')) {
    $target = Join-Path $source $forbidden
    [void][IO.Directory]::CreateDirectory((Split-Path $target -Parent))
    Set-Content $target 'fixture'
    Invoke-Checked git @('-C',$source,'add',$forbidden)
    Invoke-Checked git @('-C',$source,'commit','--quiet','-m','forbidden change')
    Expect-Failure { Assert-DocumentationUpdateChanges $source 1.2.0 } 'build/runtime configuration'
    Invoke-Checked git @('-C',$source,'rm','--quiet',$forbidden)
    Invoke-Checked git @('-C',$source,'commit','--quiet','-m','remove forbidden fixture')
}
Expect-Failure { & "$PSScriptRoot/Update-Docs.ps1" -Action Check -Tag v1.2.0 -Source $source -SourceRef main } 'full committed SHA'
Expect-Failure { & "$PSScriptRoot/Update-Docs.ps1" -Action Check -Tag v1.2.0 -Source $source -SourceRef ('b'*40) } 'differs from the selected source'
Set-Content "$source/Contract.cs" "/// <summary>Reviewed.</summary>`npublic class Changed { }"
Invoke-Checked git @('-C',$source,'commit','--quiet','-am','runtime')
Expect-Failure { Assert-DocumentationUpdateChanges $source 1.2.0 } 'Only XML'
Write-Host 'PASS: overwrite, deleted pages, successful history, stale candidate and XML-only source restriction.'
