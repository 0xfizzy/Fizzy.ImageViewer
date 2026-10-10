[CmdletBinding()]
param([ValidateSet('Development','Release')][string]$Channel = 'Development', [string]$Version, [ValidateRange(0,2147483647)][int]$Revision = 0, [string]$Package, [string]$ReviewedXml)
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Release.Common.psm1" -Force
Push-Location (Get-Repo)
try {
    if (!$Version) { $Version = Get-PackageVersion }
    if ($Channel -eq 'Release' -and $Revision -eq 0) { Assert-ReleaseIdentity $Version }
    if ($Revision -gt 0 -and (!$Package -or !$ReviewedXml)) { throw 'Revision builds require the original package and reviewed XML.' }
    & "$PSScriptRoot/Test-Documentation.ps1"
    Invoke-Checked dotnet @('tool','restore')
    $null = Reset-Output (Join-Path (Get-Repo) 'artifacts/docs')
    Invoke-Checked dotnet @('restore','Fizzy.ImageViewer.csproj','--locked-mode')
    Invoke-Checked dotnet @('build','Fizzy.ImageViewer.csproj','-c','Release','--no-restore','--no-incremental','-p:DocumentationStrict=true','-p:ContinuousIntegrationBuild=true')
    $binary = & dotnet msbuild Fizzy.ImageViewer.csproj -nologo -p:Configuration=Release -getProperty:TargetPath
    if ($LASTEXITCODE) { throw 'Cannot locate documentation assembly.' }
    $binary = ($binary -join '').Trim()
    $inputDir = Join-Path (Get-Repo) 'artifacts/docs/input'
    [void][IO.Directory]::CreateDirectory($inputDir)
    Get-ChildItem (Split-Path $binary -Parent) -File | Where-Object { $_.Extension -in @('.dll','.xml') -or $_.Name -like '*.deps.json' } | Copy-Item -Destination $inputDir
    $apiAssemblies = @('artifacts/docs/input/Fizzy.ImageViewer.dll')
    if ($Channel -eq 'Development' -and !$Package) {
        Invoke-Checked dotnet @('build','extensions/Fizzy.ImageViewer.Cuda/Fizzy.ImageViewer.Cuda.csproj','-c','Release','-p:DocumentationStrict=true','-p:ContinuousIntegrationBuild=true')
        Copy-Item extensions/Fizzy.ImageViewer.Cuda/bin/Release/net8.0-windows/Fizzy.ImageViewer.Cuda.dll $inputDir
        Copy-Item extensions/Fizzy.ImageViewer.Cuda/bin/Release/net8.0-windows/Fizzy.ImageViewer.Cuda.xml $inputDir
        $apiAssemblies += 'artifacts/docs/input/Fizzy.ImageViewer.Cuda.dll'
    }
    if ($Package) {
        $packageRoot = Join-Path (Get-Repo) 'artifacts/docs/package'
        Expand-SafeZip $Package $packageRoot
        $packageDll = @(Get-ChildItem "$packageRoot/lib" -Recurse -Filter Fizzy.ImageViewer.dll)
        if ($packageDll.Count -ne 1) { throw 'Expected one original package assembly.' }
        Copy-Item $packageDll[0].FullName $inputDir -Force
        Copy-Item ([IO.Path]::ChangeExtension($packageDll[0].FullName, '.xml')) $inputDir -Force
        if ($ReviewedXml) { Copy-Item $ReviewedXml (Join-Path $inputDir 'Fizzy.ImageViewer.xml') -Force }
    }
    [xml]$xml = Get-Content (Join-Path $inputDir 'Fizzy.ImageViewer.xml') -Raw
    $inventory = foreach ($member in $xml.doc.members.member) {
        if ($member.SelectNodes('summary').Count -gt 1) { throw "Duplicate summary: $($member.name)" }
        [pscustomobject]@{ Member = $member.name; Summary = [string]$member.summary; Inherits = $null -ne $member.SelectSingleNode('inheritdoc') }
    }
    $inventory | Export-Csv artifacts/docs/api-contracts.csv -NoTypeInformation
    Write-Json @{ metadata = @(@{ src = @(@{ src = (Get-Repo); files = $apiAssemblies }); dest = (Join-Path (Get-Repo) 'artifacts/docs/api'); disableGitFeatures = $true }) } artifacts/docs/metadata.json
    Invoke-Checked dotnet @('docfx','metadata','artifacts/docs/metadata.json','--warningsAsErrors')
    & "$PSScriptRoot/Complete-ApiNavigation.ps1"
    Invoke-Checked dotnet @('docfx','build','docfx.json','--warningsAsErrors')
    $config = Get-SiteConfig
    $directory = if ($Channel -eq 'Release') { "v$Version/r$Revision" } else { 'dev' }
    $identity = @{ version = $Version; channel = $Channel; directory = $directory; commit = (Get-Commit); basePath = $config.basePath; sdk = (& dotnet --version); docfx = '2.78.3' }
    if ($Channel -eq 'Release') { $identity.revision = $Revision }
    Write-Json $identity artifacts/docs/site/identity.json
    Write-Json (Get-Tree $inputDir) artifacts/docs/input-hashes.json
    & "$PSScriptRoot/Test-DocumentationSite.ps1"
    Invoke-Checked node @('scripts/Test-Browser.mjs')
    Write-Host 'PASS: documentation built. Preview with ./scripts/Serve-Docs.ps1'
} finally { Pop-Location }
