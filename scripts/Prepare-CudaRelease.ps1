[CmdletBinding()]
param([Parameter(Mandatory)][string]$Tag)
$ErrorActionPreference='Stop'
Import-Module "$PSScriptRoot/Release.Remote.psm1" -Force
Import-Module "$PSScriptRoot/Release.Common.psm1" -Force
if($Tag -cnotmatch '^v(.+)$'){throw 'Expected version tag.'}
$version=$Matches[1]
[void](Get-Version $version)
$matrix=@{include=@((Get-Content "$PSScriptRoot/../eng/cuda-matrix.json" -Raw | ConvertFrom-Json).variants | ForEach-Object {@{major=$_.major}})} | ConvertTo-Json -Compress
"matrix=$matrix" >> $env:GITHUB_OUTPUT
$record=Get-ReleaseRecord $Tag
if($null -eq $record){throw 'Publish the matching core release first.'}
$core=Get-ArchivedBundle $Tag
if(!(Test-NuGetPayload $core)){throw 'Matching core package is not published.'}
$repository=(Get-SiteConfig).repository
if(@($record.assets | Where-Object name -CEQ 'cuda-release-bundle.zip').Count){
    $work=Reset-Output (Join-Path (Get-Repo) 'artifacts/cuda-release-existing')
    Invoke-Checked gh @('release','download',$Tag,'--repo',$repository,'--pattern','cuda-release-bundle.zip','--dir',$work)
    Expand-SafeZip "$work/cuda-release-bundle.zip" "$work/bundle"
    & "$PSScriptRoot/Publish-CudaRelease.ps1" -Stage Validate -Tag $Tag -Bundle "$work/bundle"
    'build=false' >> $env:GITHUB_OUTPUT
}else{
    foreach($id in @('fizzy.imageviewer.cuda','fizzy.imageviewer.cuda11','fizzy.imageviewer.cuda12','fizzy.imageviewer.cuda13')){
        $probe=Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/$id/$version/$id.$version.nupkg" -Method Head -SkipHttpErrorCheck
        if($probe.StatusCode -ne 404){throw 'A CUDA package exists or its status is uncertain without an archive. Refusing to rebuild.'}
    }
    'build=true' >> $env:GITHUB_OUTPUT
}
