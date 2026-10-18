[CmdletBinding()]
param([ValidateSet('Development','Release')][string]$Channel = 'Development', [string]$Version, [string]$Package, [string]$ReviewedXml)
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Release.Common.psm1" -Force
Push-Location (Get-Repo)
try {
    if (!$Version) { $Version = Get-PackageVersion }
    if ($Channel -eq 'Release' -and !$Package) { Assert-ReleaseIdentity $Version }
    & "$PSScriptRoot/Test-Documentation.ps1"
    Invoke-Checked dotnet @('tool','restore')
    $null = Reset-Output (Join-Path (Get-Repo) 'artifacts/docs')
    Invoke-Checked dotnet @('restore','src/Fizzy.ImageViewer/Fizzy.ImageViewer.csproj','--locked-mode')
    Invoke-Checked dotnet @('build','src/Fizzy.ImageViewer/Fizzy.ImageViewer.csproj','-c','Release','--no-restore','--no-incremental','-p:DocumentationStrict=true','-p:ContinuousIntegrationBuild=true')
    $binary = & dotnet msbuild src/Fizzy.ImageViewer/Fizzy.ImageViewer.csproj -nologo -p:Configuration=Release -p:TargetFramework=net8.0-windows -getProperty:TargetPath
    if ($LASTEXITCODE) { throw 'Cannot locate documentation assembly.' }
    $binary = ($binary -join '').Trim()
    $inputDir = Join-Path (Get-Repo) 'artifacts/docs/input'
    [void][IO.Directory]::CreateDirectory($inputDir)
    Get-ChildItem (Split-Path $binary -Parent) -File | Where-Object { $_.Extension -in @('.dll','.xml') -or $_.Name -like '*.deps.json' } | Copy-Item -Destination $inputDir
    if (!$Package) {
        foreach ($framework in @('net10.0-windows','net8.0-windows')) {
            $targetBinary = & dotnet msbuild src/Fizzy.ImageViewer/Fizzy.ImageViewer.csproj -nologo -p:Configuration=Release "-p:TargetFramework=$framework" -getProperty:TargetPath
            if ($LASTEXITCODE) { throw "Cannot locate $framework documentation assembly." }
            $targetBinary = ($targetBinary -join '').Trim()
            $targetInput = Join-Path $inputDir "${framework}7.0"
            [void][IO.Directory]::CreateDirectory($targetInput)
            Copy-Item $targetBinary,([IO.Path]::ChangeExtension($targetBinary, '.xml')) $targetInput
        }
    }
    $apiAssemblies = @('artifacts/docs/input/Fizzy.ImageViewer.dll')
    if ($Channel -eq 'Development' -and !$Package) {
        Invoke-Checked dotnet @('build','src/Fizzy.ImageViewer.Cuda/Fizzy.ImageViewer.Cuda.csproj','-c','Release','-p:DocumentationStrict=true','-p:ContinuousIntegrationBuild=true')
        Copy-Item build/Release/Fizzy.ImageViewer.Cuda/net8.0-windows/Fizzy.ImageViewer.Cuda.dll $inputDir
        Copy-Item build/Release/Fizzy.ImageViewer.Cuda/net8.0-windows/Fizzy.ImageViewer.Cuda.xml $inputDir
        $apiAssemblies += 'artifacts/docs/input/Fizzy.ImageViewer.Cuda.dll'
    }
    if ($Package) {
        $packageRoot = Join-Path (Get-Repo) 'artifacts/docs/package'
        Expand-SafeZip $Package $packageRoot
        Get-ChildItem "$packageRoot/lib" -Directory | Copy-Item -Destination $inputDir -Recurse
        $packageDll = Join-Path $packageRoot 'lib/net8.0-windows7.0/Fizzy.ImageViewer.dll'
        if (!(Test-Path $packageDll)) { throw 'Expected the original net8.0-windows package assembly.' }
        Copy-Item $packageDll $inputDir -Force
        Copy-Item ([IO.Path]::ChangeExtension($packageDll, '.xml')) $inputDir -Force
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
    # DocFX requires source files beneath its config directory when exporting links.
    # Stage the site inputs so repository layout does not change published URLs.
    $siteSource = Join-Path (Get-Repo) 'artifacts/docs/source'
    [void][IO.Directory]::CreateDirectory($siteSource)
    foreach ($directory in @('docs','assets')) {
        Copy-Item $directory $siteSource -Recurse
    }
    Copy-Item README*.md,LICENSE $siteSource
    foreach ($file in Get-ChildItem samples -Recurse -File | Where-Object { $_.Extension -in @('.cs','.csproj','.xaml') -and $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }) {
        $target = Join-Path $siteSource ([IO.Path]::GetRelativePath((Get-Repo), $file.FullName))
        [void][IO.Directory]::CreateDirectory((Split-Path $target -Parent))
        Copy-Item $file.FullName $target
    }
    $apiSource = Join-Path $siteSource 'artifacts/docs'
    [void][IO.Directory]::CreateDirectory($apiSource)
    Copy-Item artifacts/docs/api $apiSource -Recurse
    Copy-Item docs/docfx.json (Join-Path $siteSource 'docfx.json')
    Invoke-Checked dotnet @('docfx','build',(Join-Path $siteSource 'docfx.json'),'--warningsAsErrors')
    Copy-Item (Join-Path $PSScriptRoot '../docs/site/versions.js') 'artifacts/docs/site/docs/site/versions.js' -Force
    $config = Get-SiteConfig
    $directory = if ($Channel -eq 'Release') { "v$Version" } else { 'dev' }
    $identity = @{ version = $Version; channel = $Channel; directory = $directory; commit = (Get-Commit); basePath = $config.basePath; sdk = (& dotnet --version); docfx = '2.78.3' }
    Write-Json $identity artifacts/docs/site/identity.json
    Write-Json (Get-Tree $inputDir) artifacts/docs/input-hashes.json
    & "$PSScriptRoot/Test-DocumentationSite.ps1"
    Invoke-Checked node @((Join-Path $PSScriptRoot 'Test-Browser.mjs'), '--site', (Join-Path (Get-Repo) 'artifacts/docs/site'))
    Write-Host 'PASS: documentation built. Preview with ./scripts/Serve-Docs.ps1'
} finally { Pop-Location }
