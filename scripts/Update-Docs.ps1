[CmdletBinding()]
param(
 [Parameter(Mandatory)][ValidateSet('Prepare','Check','Start','Status','Resume','Rollback')][string]$Action,
 [string]$Tag, [string]$SourceRef, [string]$RunId, [string]$ArchiveCommit,
 [string]$Source = '.', [string]$Branch
)
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Release.Common.psm1" -Force
Import-Module "$PSScriptRoot/Release.Remote.psm1" -Force
Import-Module "$PSScriptRoot/DocumentationUpdate.Common.psm1" -Force
$config = Get-SiteConfig
if ($Action -eq 'Status') {
 if ($RunId) { Invoke-Checked gh @('run','view',$RunId,'--repo',$config.repository) }
 else { Invoke-Checked gh @('run','list','--repo',$config.repository,'--workflow','docs-update.yml','--limit','10') }
 return
}
if ($Action -eq 'Resume') {
 if ($RunId -cnotmatch '^\d+$') { throw 'Resume requires RunId.' }
 Invoke-Checked gh @('workflow','run','docs-update.yml','--repo',$config.repository,'--ref','main','-f','action=resume','-f',"run_id=$RunId")
 return
}
if ($Tag -cnotmatch '^v(.+)$') { throw 'Tag must start with v.' }
$Version = $Tag.Substring(1); [void](Get-Version $Version)
function Get-ArchiveHead {
 $value = & gh api "repos/$($config.repository)/git/ref/heads/$($config.archiveBranch)" --jq .object.sha
 if ($LASTEXITCODE -or $value -cnotmatch '^[0-9a-f]{40}$') { throw 'Cannot read archive baseline.' }
 $value
}
$stateFile = Join-Path (Get-Repo) 'artifacts/docs-update/prepared.json'
if ($Action -eq 'Prepare') {
 $dirty = & git status --porcelain
 if ($LASTEXITCODE -or $dirty) { throw 'Prepare requires a clean checkout.' }
 $baseline = Get-ArchiveHead
 Invoke-Checked git @('fetch','origin',$baseline,"refs/tags/${Tag}:refs/tags/${Tag}")
 $index = (& git show "${baseline}:versions.json") -join "`n" | ConvertFrom-Json
 if ($LASTEXITCODE) { throw 'Cannot inspect archive.' }
 $entry = @($index.versions | Where-Object { $_.version -ceq $Version -and $_.completed })
 if ($entry.Count -ne 1) { throw 'A completed package version is required.' }
 $base = if ($SourceRef) { $SourceRef } else { $entry[0].commit }
 Invoke-Checked git @('fetch','origin',$base)
 if (!$Branch) { $Branch = "docs/$Tag-$(Get-Date -Format yyyyMMddHHmmss)" }
 $checkout = Join-Path (Get-Repo) "artifacts/docs-update/source-$($Branch.Replace('/','-'))"
 Invoke-Checked git @('worktree','add','-b',$Branch,$checkout,$base)
 Write-Json @{ tag=$Tag; baseline=$baseline; source=$checkout } $stateFile
 Write-Host "Prepared $Branch at $checkout. Edit and commit there; run Check and Start from this trusted tooling checkout."
 return
}
if ($Action -in @('Start','Rollback')) {
 $baseline = if (Test-Path $stateFile) {
  $state = Get-Content $stateFile -Raw | ConvertFrom-Json
  if ($state.tag -cne $Tag) { throw 'Prepared tag mismatch.' }; $state.baseline
 } else { Get-ArchiveHead }
 $arguments = @('workflow','run','docs-update.yml','--repo',$config.repository,'--ref','main','-f',"action=$($Action.ToLowerInvariant())",'-f',"tag=$Tag",'-f',"expected_archive=$baseline")
 if ($Action -eq 'Start') {
  if (!$SourceRef) { throw 'Start requires SourceRef from the reviewed documentation checkout.' }
  if ($SourceRef -cnotmatch '^[0-9a-f]{40}$') { throw 'SourceRef must be a full committed SHA.' }
  $arguments += @('-f',"source_ref=$SourceRef")
 } else {
  if ($ArchiveCommit -cnotmatch '^[0-9a-f]{40}$') { throw 'Rollback requires a successful archive commit SHA.' }
  $arguments += @('-f',"archive_commit=$ArchiveCommit")
 }
 Invoke-Checked gh $arguments
 return
}
if ($Source -eq '.' -and (Test-Path $stateFile)) {
 $prepared = Get-Content $stateFile -Raw | ConvertFrom-Json
 if ($prepared.tag -cne $Tag) { throw 'Prepared tag mismatch.' }; $Source = $prepared.source
}
$Source = Resolve-FullPath $Source
if ($SourceRef) {
 if ($SourceRef -cnotmatch '^[0-9a-f]{40}$') { throw 'Check SourceRef must be a full committed SHA.' }
 $sourceHead = & git -C $Source rev-parse HEAD
 if ($LASTEXITCODE -or $sourceHead -cne $SourceRef) { throw 'Check SourceRef differs from the selected source checkout HEAD. Use -Source or Prepare to select that committed source.' }
}
Assert-DocumentationUpdateChanges $Source $Version
$original = Get-ArchivedBundle $Tag
$manifest = Assert-Bundle $original
if (!(Test-NuGetPayload $original)) { throw 'Original published package unavailable.' }
$record = Get-ReleaseRecord $Tag
if ($record.draft) { throw 'Complete the original release first.' }
$package = @(Get-ChildItem $original -Filter '*.nupkg')[0].FullName
$previous = $env:DOCS_SOURCE_ROOT
try {
 $env:DOCS_SOURCE_ROOT = $Source
 Import-Module "$PSScriptRoot/Release.Common.psm1" -Force
 Invoke-Checked dotnet @('build',(Join-Path $Source 'src/Fizzy.ImageViewer/Fizzy.ImageViewer.csproj'),'-c','Release','--no-incremental','-p:DocumentationStrict=true')
 $binary = & dotnet msbuild (Join-Path $Source 'src/Fizzy.ImageViewer/Fizzy.ImageViewer.csproj') -nologo -p:Configuration=Release -getProperty:TargetPath
 if ($LASTEXITCODE) { throw 'Cannot locate reviewed XML.' }
 $xml = [IO.Path]::ChangeExtension(($binary -join '').Trim(), '.xml')
 & "$PSScriptRoot/Build-Docs.ps1" -Channel Release -Version $Version -Package $package -ReviewedXml $xml
 & "$PSScriptRoot/Test-Samples.ps1" -Package $package
 $bundle = Reset-Output (Join-Path $Source 'artifacts/docs-update-bundle')
 Copy-Item (Join-Path $Source 'artifacts/docs/site') (Join-Path $bundle 'site') -Recurse
 Write-Json @{ schema=1; tag=$Tag; version=$Version; commit=(Get-Commit); originalCommit=$manifest.commit; packageHash=(Get-Hash $package); files=(Get-Tree (Join-Path $bundle 'site')) } (Join-Path $bundle 'update.json')
} finally { $env:DOCS_SOURCE_ROOT = $previous }
Write-Host 'PASS: original DLL, reviewed XML, package samples and documentation checks.'
