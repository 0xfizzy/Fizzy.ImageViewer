[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Prepare','Check','Start','Status','Resume','Rollback','ValidateSource','Stage','Verify','Activate')][string]$Action,
    [Parameter(Mandatory)][string]$Version,
    [ValidateRange(0,2147483647)][int]$Revision = 0,
    [string]$SourceRef,
    [string]$Source = '.',
    [string]$Archive = 'artifacts/archive',
    [string]$Bundle = 'artifacts/revision-bundle',
    [switch]$RequireActive
)
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Release.Common.psm1" -Force
Import-Module "$PSScriptRoot/Release.Remote.psm1" -Force
Import-Module "$PSScriptRoot/DocumentationRevision.Common.psm1" -Force
[void](Get-Version $Version)
$config = Get-SiteConfig
if ($Action -eq 'Status') {
    Invoke-Checked gh @('run','list','--repo',$config.repository,'--workflow','docs-revision.yml','--limit','10')
    return
}
if ($Action -eq 'Prepare') {
    $dirty = & git -C (Get-Repo) status --porcelain
    if ($LASTEXITCODE -or $dirty) { throw 'Prepare requires a clean checkout before switching to a documentation branch.' }
    $indexPath = Join-Path (Get-Repo) 'artifacts/revision-index.json'
    [void][IO.Directory]::CreateDirectory((Split-Path $indexPath -Parent))
    $null = Get-RemoteFile "$($config.origin)$($config.basePath)versions.json" $indexPath
    $index = Get-Content $indexPath -Raw | ConvertFrom-Json
    $entry = @($index.versions | Where-Object version -CEQ $Version)
    if ($entry.Count -ne 1 -or !$entry[0].completed -or !($entry[0].PSObject.Properties.Name -contains 'revisions')) { throw 'A completed revision-capable release is required.' }
    $Revision = 1 + ($entry[0].revisions.revision | Measure-Object -Maximum).Maximum
    $branch = if ($SourceRef) { $SourceRef } else { "docs/v$Version-r$Revision" }
    Invoke-Checked git @('-C',(Get-Repo),'fetch','origin',"refs/tags/v${Version}:refs/tags/v${Version}")
    $active = @($entry[0].revisions | Where-Object revision -EQ $entry[0].currentRevision)
    if ($active.Count -ne 1 -or $active[0].commit -cnotmatch '^[0-9a-f]{40}$') { throw 'Invalid active revision source identity.' }
    Invoke-Checked git @('-C',(Get-Repo),'fetch','origin',$active[0].commit)
    Invoke-Checked git @('-C',(Get-Repo),'switch','-c',$branch,$active[0].commit)
    Write-Host "Prepared $branch in the current checkout. Edit, review translations, commit, then Check and push this branch. Suggested revision: $Revision; allocation is checked again under the deployment lock."
    return
}
if ($Action -in @('Start','Resume','Rollback')) {
    if ($Action -ne 'Rollback' -and $Revision -lt 1) { throw 'Corrections start at revision 1.' }
    $arguments = @('workflow','run','docs-revision.yml','--repo',$config.repository,'--ref','main','-f',"action=$($Action.ToLowerInvariant())",'-f',"version=$Version",'-f',"revision=$Revision")
    if ($Action -eq 'Start') {
        if (!$SourceRef) { $SourceRef = Get-Commit }
        $arguments += @('-f',"source_ref=$SourceRef")
    }
    Invoke-Checked gh $arguments
    return
}
if ($Action -eq 'ValidateSource') { Assert-DocumentationRevisionChanges (Resolve-FullPath $Source) $Version; return }
if ($Action -eq 'Check') {
    if ($Revision -lt 1) { throw 'Corrections start at revision 1.' }
    Assert-DocumentationRevisionChanges (Get-Repo) $Version
    $original = Get-ArchivedBundle "v$Version"
    $originalManifest = Assert-Bundle $original
    if (!(Test-NuGetPayload $original)) { throw 'Original published package unavailable.' }
    $package = @(Get-ChildItem $original -Filter '*.nupkg')[0].FullName
    # Strictly compile reviewed source XML; runtime edits have already been rejected.
    Invoke-Checked dotnet @('build',(Join-Path (Get-Repo) 'Fizzy.ImageViewer.csproj'),'-c','Release','--no-incremental','-p:DocumentationStrict=true')
    $binary = & dotnet msbuild (Join-Path (Get-Repo) 'Fizzy.ImageViewer.csproj') -nologo -p:Configuration=Release -getProperty:TargetPath
    if ($LASTEXITCODE) { throw 'Cannot locate reviewed XML.' }
    $xml = [IO.Path]::ChangeExtension(($binary -join '').Trim(), '.xml')
    & "$PSScriptRoot/Build-Docs.ps1" -Channel Release -Version $Version -Revision $Revision -Package $package -ReviewedXml $xml
    & "$PSScriptRoot/Test-Samples.ps1" -Package $package
    $Bundle = Reset-Output (Resolve-FullPath $Bundle)
    Copy-Item (Join-Path (Get-Repo) 'artifacts/docs/site') (Join-Path $Bundle 'site') -Recurse
    Write-Json @{ schema = 1; version = $Version; revision = $Revision; commit = (Get-Commit); originalCommit = $originalManifest.commit; packageHash = (Get-Hash $package); files = (Get-Tree (Join-Path $Bundle 'site')) } (Join-Path $Bundle 'revision.json')
    Write-Host 'PASS: immutable original DLL, reviewed XML, original-package samples and browser smoke verified.'
    return
}
if ($Action -eq 'Stage') {
    if ($Revision -lt 1) { throw 'Corrections start at revision 1.' }
    $original = Get-ArchivedBundle "v$Version"
    $originalManifest = Assert-Bundle $original
    $index = Get-Content (Join-Path $Archive 'versions.json') -Raw | ConvertFrom-Json
    $entry = @($index.versions | Where-Object version -CEQ $Version)
    if ($entry.Count -ne 1 -or !$entry[0].completed -or !($entry[0].PSObject.Properties.Name -contains 'revisions')) { throw 'A completed revision-capable original release is required.' }
    $package = @(Get-ChildItem $original -Filter '*.nupkg')[0].FullName
    $asset = "documentation-r$Revision.zip"
    $record = Get-ReleaseRecord "v$Version"
    if ($record.draft) { throw 'Complete the original package release before correcting documentation.' }
    $download = Reset-Output (Join-Path (Get-Repo) 'artifacts/revision-archive')
    if (@($record.assets | Where-Object name -CEQ $asset).Count) {
        Invoke-Checked gh @('release','download',"v$Version",'--repo',$config.repository,'--pattern',$asset,'--dir',$download)
        Expand-SafeZip (Join-Path $download $asset) (Join-Path $download 'bundle')
        if (Test-Path (Join-Path $Bundle 'revision.json')) { Assert-Tree (Get-Tree $Bundle) (Join-Path $download 'bundle') }
        $Bundle = Join-Path $download 'bundle'
    } else {
        $candidate = Assert-RevisionBundle $Bundle $Version $Revision
        if ($candidate.originalCommit -cne $originalManifest.commit -or $candidate.packageHash -cne (Get-Hash $package)) { throw 'Revision original-package identity conflict.' }
        [IO.Compression.ZipFile]::CreateFromDirectory((Resolve-FullPath $Bundle), (Join-Path $download $asset))
        Invoke-Checked gh @('release','upload',"v$Version",(Join-Path $download $asset),'--repo',$config.repository)
        $verify = Join-Path $download 'verify'
        [void][IO.Directory]::CreateDirectory($verify)
        Invoke-Checked gh @('release','download',"v$Version",'--repo',$config.repository,'--pattern',$asset,'--dir',$verify)
        Expand-SafeZip (Join-Path $verify $asset) (Join-Path $verify 'bundle')
        Assert-Tree (Get-Tree $Bundle) (Join-Path $verify 'bundle')
    }
    $manifest = Assert-RevisionBundle $Bundle $Version $Revision
    if ($manifest.originalCommit -cne $originalManifest.commit -or $manifest.packageHash -cne (Get-Hash $package)) { throw 'Revision original-package identity conflict.' }
    & "$PSScriptRoot/Merge-DocumentationArchive.ps1" -Archive $Archive -Site (Join-Path $Bundle 'site')
    return
}
if ($Action -eq 'Verify') { Test-OnlineRevision $Archive $Version $Revision -RequireActive:$RequireActive; return }
if ($Action -eq 'Activate') {
    Test-OnlineRevision $Archive $Version $Revision
    & "$PSScriptRoot/Merge-DocumentationArchive.ps1" -Archive $Archive -Version $Version -Revision $Revision -Complete
}
