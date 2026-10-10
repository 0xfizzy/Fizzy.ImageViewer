[CmdletBinding(SupportsShouldProcess)]
param()
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Release.Common.psm1" -Force
$config = Get-SiteConfig
$remote = "https://github.com/$($config.repository).git"
$existing = & git ls-remote --heads $remote $config.archiveBranch
if ($LASTEXITCODE) { throw 'Cannot inspect remote; authenticate Git before initialization.' }
if ($existing) { throw 'Archive branch already exists; initialization will not replace it.' }
if (!$PSCmdlet.ShouldProcess($remote, "Create $($config.archiveBranch)")) { return }
$work = Reset-Output (Join-Path (Get-Repo) 'artifacts/archive-initialization')
Invoke-Checked git @('init','--initial-branch',$config.archiveBranch,$work)
Invoke-Checked git @('-C',$work,'remote','add','origin',$remote)
& "$PSScriptRoot/Merge-DocumentationArchive.ps1" -Archive $work
& "$PSScriptRoot/Save-DocumentationArchive.ps1" -Archive $work -Message 'docs: initialize immutable documentation archive'
