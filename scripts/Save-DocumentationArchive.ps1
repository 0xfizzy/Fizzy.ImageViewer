[CmdletBinding()]
param([Parameter(Mandatory)][string]$Archive, [Parameter(Mandatory)][string]$Message)
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Release.Common.psm1" -Force
$branch = & git -C $Archive branch --show-current
if ($LASTEXITCODE -or $branch -cne (Get-SiteConfig).archiveBranch) { throw 'Not on the documentation archive branch.' }
Invoke-Checked git @('-C',$Archive,'config','user.name','github-actions[bot]')
Invoke-Checked git @('-C',$Archive,'config','user.email','41898282+github-actions[bot]@users.noreply.github.com')
Invoke-Checked git @('-C',$Archive,'add','--all')
& git -C $Archive diff --cached --quiet
if ($LASTEXITCODE -eq 1) { Invoke-Checked git @('-C',$Archive,'commit','-m',$Message) }
elseif ($LASTEXITCODE -ne 0) { throw 'Cannot inspect archive changes.' }
Invoke-Checked git @('-C',$Archive,'push','origin',"HEAD:$((Get-SiteConfig).archiveBranch)")
