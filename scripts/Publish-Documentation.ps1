[CmdletBinding()]
param(
 [Parameter(Mandatory)][ValidateSet('Stage','Restore','Success','Verify','Baseline','StageDevelopment')][string]$Action,
 [string]$Archive='artifacts/archive', [string]$Bundle, [string]$RunId=$env:GITHUB_RUN_ID,
 [string]$ExpectedArchive, [string]$ArchiveCommit, [string]$Tag, [string]$ResumeRunId
)
$ErrorActionPreference='Stop'
Import-Module "$PSScriptRoot/Release.Common.psm1" -Force
Import-Module "$PSScriptRoot/Release.Remote.psm1" -Force
$Archive=Resolve-FullPath $Archive
$control=Join-Path $Archive '.publication'
$statePath=Join-Path $control 'state.json'
$state=if(Test-Path $statePath){Get-Content $statePath -Raw | ConvertFrom-Json -AsHashtable}else{@{successes=@()}}
function Head { $sha=& git -C $Archive rev-parse HEAD; if($LASTEXITCODE){throw 'Cannot resolve archive HEAD.'}; $sha }
function Restore-Public([string]$Commit) {
 if($Commit -cnotmatch '^[0-9a-f]{40}$'){throw 'Invalid archive commit.'}
 $temp=Reset-Output (Join-Path (Get-Repo) 'artifacts/archive-restore')
 Invoke-Checked git @('-C',$Archive,'archive','--format=zip',"--output=$temp/snapshot.zip",$Commit)
 Expand-SafeZip "$temp/snapshot.zip" "$temp/tree"
 foreach($item in Get-ChildItem -LiteralPath $Archive -Force | Where-Object Name -NotIn @('.git','.publication')) {
  Remove-Item -LiteralPath (Assert-ChildPath $Archive $item.FullName) -Recurse -Force
 }
 foreach($item in Get-ChildItem "$temp/tree" -Force | Where-Object Name -NotIn @('.git','.publication')) { Copy-Item $item.FullName $Archive -Recurse }
}
if($Action -eq 'Baseline') {
 if($ExpectedArchive -cnotmatch '^[0-9a-f]{40}$' -or (Head) -cne $ExpectedArchive){throw 'Stale archive baseline; prepare the update again.'}
 return
}
if($Action -eq 'Restore') {
 if($state.ContainsKey('lastSuccess')){Restore-Public $state.lastSuccess}
 return
}
if($Action -eq 'StageDevelopment') {
 $ExpectedArchive=Head
 $site=Join-Path (Get-Repo) 'artifacts/docs/site'
 $identity=Get-Content (Join-Path $site 'identity.json') -Raw | ConvertFrom-Json
 if($identity.channel -cne 'Development'){throw 'Expected development site.'}
 $Bundle=Reset-Output (Join-Path (Get-Repo) 'artifacts/development-update-bundle')
 Copy-Item $site (Join-Path $Bundle 'site') -Recurse
 Write-Json @{schema=1;tag='dev';version=$identity.version;commit=$identity.commit;channel='Development';files=(Get-Tree (Join-Path $Bundle 'site'))} (Join-Path $Bundle 'update.json')
 $Action='Stage'
}
if($Action -eq 'Stage') {
 if($RunId -cnotmatch '^\d+$'){throw 'Invalid run ID.'}
 $current=Head
 if(!$ResumeRunId -and ($ExpectedArchive -cnotmatch '^[0-9a-f]{40}$' -or $ExpectedArchive -cne $current)){throw 'Stale archive baseline; prepare the update again.'}
 # A failed candidate never becomes the base of an unrelated subsequent deployment.
 if($state.ContainsKey('lastSuccess')){Restore-Public $state.lastSuccess}
 else {
  $state.lastSuccess=$current
  $state.successes+=@{commit=$current;runId='migration-baseline'}
 }
 $runRoot=Join-Path $control "runs/$RunId"
 if(Test-Path $runRoot){throw 'Run already staged. Use Resume with its run ID.'}
 if($ResumeRunId){
  if($ResumeRunId -cnotmatch '^\d+$'){throw 'Invalid resume run ID.'}
  $original=Join-Path $control "runs/$ResumeRunId"
  $prior=Get-Content (Join-Path $original 'request.json') -Raw | ConvertFrom-Json -AsHashtable
  # Only the failed candidate at the current successful base may be retried.
  if($prior.baseSuccess -cne $state.lastSuccess){throw 'Stale candidate; a later deployment already succeeded.'}
  $Bundle=Join-Path $original 'bundle'
 } elseif($ArchiveCommit){
  if($ArchiveCommit -cnotmatch '^[0-9a-f]{40}$' -or !@($state.successes | Where-Object commit -CEQ $ArchiveCommit).Count){throw 'Rollback requires a successful deployed archive commit.'}
  if($Tag -cnotmatch '^v(.+)$'){throw 'Invalid tag.'}; [void](Get-Version $Tag.Substring(1))
  $temp=Reset-Output (Join-Path (Get-Repo) 'artifacts/docs-rollback')
  Invoke-Checked git @('-C',$Archive,'archive','--format=zip',"--output=$temp/snapshot.zip",$ArchiveCommit)
  Expand-SafeZip "$temp/snapshot.zip" "$temp/tree"
  & "$PSScriptRoot/Migrate-DocumentationArchive.ps1" -Archive "$temp/tree"
  $Bundle=Join-Path $temp 'bundle'; [void][IO.Directory]::CreateDirectory($Bundle)
  Copy-Item "$temp/tree/$Tag" "$Bundle/site" -Recurse
  $id=Get-Content "$Bundle/site/identity.json" -Raw | ConvertFrom-Json
  Write-Json @{schema=1;tag=$Tag;version=$Tag.Substring(1);commit=$id.commit;files=(Get-Tree "$Bundle/site");rollback=$ArchiveCommit} "$Bundle/update.json"
 }
 if(!$Bundle){throw 'Bundle is required.'}
 $manifest=Get-Content (Join-Path $Bundle 'update.json') -Raw | ConvertFrom-Json -AsHashtable
 if($manifest.tag -ne 'dev'){[void](Get-Version $manifest.version)}
 Assert-Tree $manifest.files (Join-Path $Bundle 'site')
 $id=Get-Content (Join-Path $Bundle 'site/identity.json') -Raw | ConvertFrom-Json
 if($manifest.schema -ne 1 -or ($manifest.tag -cne 'dev' -and $manifest.tag -cne "v$($manifest.version)") -or $id.directory -cne $manifest.tag -or $id.commit -cne $manifest.commit){throw 'Update bundle identity mismatch.'}
 if($manifest.tag -eq 'dev'){
  Invoke-Checked git @('-C',(Get-Repo),'fetch','origin','main')
  $main=& git -C (Get-Repo) rev-parse origin/main
  if($LASTEXITCODE -or $main -cne $manifest.commit -or $id.channel -cne 'Development'){throw 'Stale development candidate.'}
 } elseif(!$manifest.ContainsKey('rollback')){
  $original=Get-ArchivedBundle $manifest.tag
  $release=Assert-Bundle $original
  $package=@(Get-ChildItem $original -Filter '*.nupkg')[0].FullName
  if($release.commit -cne $manifest.originalCommit -or (Get-Hash $package) -cne $manifest.packageHash -or (Get-ReleaseRecord $manifest.tag).draft){throw 'Original package provenance mismatch.'}
 }
 [void][IO.Directory]::CreateDirectory($runRoot)
 Copy-Item $Bundle (Join-Path $runRoot 'bundle') -Recurse
 Assert-Tree (Get-Tree $Bundle) (Join-Path $runRoot 'bundle')
 Write-Json @{baseSuccess=$state.lastSuccess;expectedArchive=$ExpectedArchive;tag=$manifest.tag;runId=$RunId;resumedFrom=$ResumeRunId;files=(Get-Tree (Join-Path $runRoot 'bundle'))} (Join-Path $runRoot 'request.json')
 Write-Json $state $statePath
 if($manifest.tag -eq 'dev') { & "$PSScriptRoot/Merge-DocumentationArchive.ps1" -Archive $Archive -Site (Join-Path $runRoot 'bundle/site') }
 else { & "$PSScriptRoot/Merge-DocumentationArchive.ps1" -Archive $Archive -Site (Join-Path $runRoot 'bundle/site') -Complete -Version $manifest.version }
 return
}
if($Action -eq 'Verify') {
 $config=Get-SiteConfig
 $work=Reset-Output (Join-Path (Get-Repo) 'artifacts/update-online')
 $files=@('versions.json','index.html','latest/index.html')
 $index=Get-Content (Join-Path $Archive 'versions.json') -Raw | ConvertFrom-Json
 if($Tag){$files+=@((Get-Tree (Join-Path $Archive $Tag)).Keys | ForEach-Object{"$Tag/$_"})}
 else {
  if($index.development){$files+=@('dev/identity.json','dev/index.html')}
  foreach($version in $index.versions){$files+=@("v$($version.version)/identity.json","v$($version.version)/index.html")}
 }
 foreach($file in $files){
  $target=Join-Path $work ($file.Replace('/','_'))
  $matched=$false
  for($i=0;$i -lt 6;$i++){
   $exists=Get-RemoteFile "$($config.origin)$($config.basePath)$file" $target -AllowMissing
   if($exists -and (Get-Hash $target) -ceq (Get-Hash (Join-Path $Archive $file))){$matched=$true;break}
   Start-Sleep -Seconds 5
  }
  if(!$matched){throw "Deployed file mismatch: $file"}
 }
 return
}
if($Action -eq 'Success') {
 $commit=Head
 $state.lastSuccess=$commit
 $state.successes=@($state.successes | Where-Object commit -CNE $commit)+@{commit=$commit;runId=$RunId}
 Write-Json $state $statePath
}
