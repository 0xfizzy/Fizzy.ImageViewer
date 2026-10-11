[CmdletBinding()]
param([string]$Version, [switch]$Release)
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Release.Common.psm1" -Force
Push-Location (Get-Repo)
try {
    $actual = Get-PackageVersion
    if (!$Version) { $Version = $actual }
    if ($Version -cne $actual) { throw 'Requested version differs from project.' }
    if ($Release) { Assert-ReleaseIdentity $Version }
    $output = Reset-Output (Join-Path (Get-Repo) 'artifacts/release')
    Start-Transcript (Join-Path $output 'validation.log') | Out-Null
    try {
        & "$PSScriptRoot/Test-Workflows.ps1"
        foreach ($project in @('src/Fizzy.ImageViewer/Fizzy.ImageViewer.csproj','tests/Fizzy.ImageViewer.Tests/Fizzy.ImageViewer.Tests.csproj')) { Invoke-Checked dotnet @('restore',$project,'--locked-mode') }
        Invoke-Checked dotnet @('test','tests/Fizzy.ImageViewer.Tests/Fizzy.ImageViewer.Tests.csproj','-c','Release','--no-restore','--logger',"trx;LogFileName=release.trx",'--results-directory',"$output/tests")
        foreach ($project in Get-ChildItem tools -Recurse -Filter '*.csproj') {
            Invoke-Checked dotnet @('restore',$project.FullName,'--locked-mode')
            Invoke-Checked dotnet @('build',$project.FullName,'-c','Release','--no-restore')
        }
        & "$PSScriptRoot/Test-Samples.ps1"
        $channel = if ($Release) { 'Release' } else { 'Development' }
        & "$PSScriptRoot/Build-Docs.ps1" -Channel $channel -Version $Version
        & "$PSScriptRoot/Test-DocumentationGate.ps1"
        & "$PSScriptRoot/Test-ReleaseGate.ps1"
        & "$PSScriptRoot/Test-DocumentationUpdate.ps1"
        Invoke-Checked pwsh @('-NoProfile','-File',"$PSScriptRoot/Test-ReleaseRecovery.ps1")
        $config = Get-SiteConfig
        $readme = (Get-Content docs/nuget-readme.md -Raw).Replace("$($config.origin)$($config.basePath)", "$($config.origin)$($config.basePath)v$Version/").Replace('/blob/main/LICENSE', "/blob/$(Get-Commit)/LICENSE")
        $readmePath = Join-Path $output 'README.md'
        Set-Content $readmePath $readme -Encoding utf8NoBOM
        Invoke-Checked dotnet @('pack','src/Fizzy.ImageViewer/Fizzy.ImageViewer.csproj','-c','Release','--no-build','--no-restore','-o',"$output/packages","-p:ReleaseReadme=$readmePath",'-p:ContinuousIntegrationBuild=true')
        $packages = @(Get-ChildItem "$output/packages" -Filter '*.nupkg')
        if ($packages.Count -ne 1) { throw 'Expected exactly one package.' }
        if ($Release) {
            & "$PSScriptRoot/Build-Docs.ps1" -Channel Release -Version $Version -Package $packages[0].FullName
        }
        & "$PSScriptRoot/Test-Package.ps1" -Package $packages[0].FullName -Version $Version -DocumentationInput artifacts/docs/input
        Write-Json @{ version = $Version; commit = (Get-Commit); release = [bool]$Release; passed = $true; packageHash = (Get-Hash $packages[0].FullName); siteFiles = (Get-Tree artifacts/docs/site); unverified = @('physical display','mixed DPI','real GPU/devices','external service deployment') } "$output/validation.json"
    } finally { Stop-Transcript | Out-Null }
} finally { Pop-Location }
