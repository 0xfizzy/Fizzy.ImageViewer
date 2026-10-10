[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('Archive','NuGet','Complete')][string]$Stage, [string]$Bundle = 'artifacts/bundle')
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Release.Common.psm1" -Force
Import-Module "$PSScriptRoot/Release.Remote.psm1" -Force
$Bundle = Resolve-FullPath $Bundle
$manifest = Assert-Bundle $Bundle
$config = Get-SiteConfig
$record = Get-ReleaseRecord $manifest.tag
if ($Stage -eq 'Archive') {
    if ($null -eq $record) {
        Invoke-Checked gh @('release','create',$manifest.tag,'--repo',$config.repository,'--verify-tag','--draft','--title',$manifest.tag,'--notes',"Immutable package and documentation for $($manifest.tag). Publication is incomplete until all checks pass.")
        $record = Get-ReleaseRecord $manifest.tag
        for ($attempt = 0; $null -eq $record -and $attempt -lt 5; $attempt++) {
            Start-Sleep -Seconds 2
            $record = Get-ReleaseRecord $manifest.tag
        }
        if ($null -eq $record) { throw 'Created release is not visible yet. Retry with the original preparation artifact.' }
    }
    $assets = @($record.assets | Where-Object name -CEQ 'release-bundle.zip')
    if (!$assets.Count) {
        if (!$record.draft) { throw 'A published Release without its archive must be repaired manually.' }
        $staging = Reset-Output (Join-Path (Get-Repo) 'artifacts/upload')
        [IO.Compression.ZipFile]::CreateFromDirectory($Bundle, "$staging/release-bundle.zip")
        Invoke-Checked gh @('release','upload',$manifest.tag,"$staging/release-bundle.zip",'--repo',$config.repository)
    }
    $remote = Get-ArchivedBundle $manifest.tag
    Assert-Tree (Get-Tree $Bundle) $remote
    Write-Host 'PASS: original bundle durably archived and downloaded for verification.'
} elseif ($Stage -eq 'NuGet') {
    # Verify durable storage before performing the irreversible package publication.
    $remote = Get-ArchivedBundle $manifest.tag
    Assert-Tree (Get-Tree $Bundle) $remote
    if (!(Test-NuGetPayload $Bundle -AllowMissing)) {
        if (!$env:NUGET_API_KEY) { throw 'NUGET_API_KEY must be supplied by the OIDC workflow step.' }
        $package = @(Get-ChildItem $Bundle -Filter '*.nupkg')[0]
        # Do not print arguments containing credentials or use --skip-duplicate.
        & dotnet nuget push $package.FullName --api-key $env:NUGET_API_KEY --source https://api.nuget.org/v3/index.json --timeout 120
        $pushExit = $LASTEXITCODE
        $visible = $false
        for ($attempt = 0; $attempt -lt 60; $attempt++) {
            if (Test-NuGetPayload $Bundle -AllowMissing) { $visible = $true; break }
            Start-Sleep -Seconds 10
        }
        if (!$visible) { throw "NuGet publication not verified (push exit $pushExit). Keep the draft; resume this tag after checking indexing." }
    }
    Write-Host 'PASS: NuGet payload matches the archived package.'
} else {
    & "$PSScriptRoot/Test-PublishedRelease.ps1" -Bundle $Bundle -RequireCompleted
    $record = Get-ReleaseRecord $manifest.tag
    if ($null -eq $record) { throw 'Archived GitHub Release disappeared.' }
    if ($record.draft) {
        $arguments = @('release','edit',$manifest.tag,'--repo',$config.repository,'--draft=false','--notes',"Package and versioned documentation verified: $($config.origin)$($config.basePath)$($manifest.tag)/")
        if ($manifest.version.Contains('-')) { $arguments += '--prerelease' }
        Invoke-Checked gh $arguments
    }
    Write-Host "PASS: $($manifest.tag) complete: $($config.origin)$($config.basePath)$($manifest.tag)/"
}
