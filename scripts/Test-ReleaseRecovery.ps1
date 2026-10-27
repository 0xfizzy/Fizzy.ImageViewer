[CmdletBinding()]
param([ValidateSet('1.2.0','1.3.0')][string]$Version = '1.2.0')
# Run in a separate pwsh process: all network/CLI boundaries are replaced by in-process fakes.
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Release.Common.psm1" -Force
$work = Reset-Output (Join-Path (Get-Repo) "artifacts/recovery-fixture/$Version")
$bundle = Join-Path $work 'original'
$site = Join-Path $work 'site'
[void][IO.Directory]::CreateDirectory($bundle)
[void][IO.Directory]::CreateDirectory($site)
$commit = 'a' * 40
Write-Json @{ version = $Version; directory = "v$Version"; channel = 'Release'; commit = $commit; basePath = (Get-SiteConfig).basePath } "$site/identity.json"
Set-Content "$site/index.html" '<html>immutable test documentation</html>'
if ($Version -eq '1.3.0') {
    foreach ($language in @('en','zh-CN')) {
        [void][IO.Directory]::CreateDirectory("$site/docs/$language")
        Set-Content "$site/docs/$language/upgrading.html" '<html>migration fixture</html>'
    }
}
$payload = Join-Path $work 'payload'
[void][IO.Directory]::CreateDirectory($payload)
Set-Content "$payload/library.dll" 'fixture package bytes'
[IO.Compression.ZipFile]::CreateFromDirectory($payload, "$bundle/Fizzy.ImageViewer.$Version.nupkg")
[IO.Compression.ZipFile]::CreateFromDirectory($site, "$bundle/documentation.zip")
Write-Json @{ version = $Version; commit = $commit; passed = $true; release = $true; siteFiles = (Get-Tree $site) } "$bundle/validation.json"
Write-Json @{ schema = 1; version = $Version; tag = "v$Version"; commit = $commit; files = (Get-Tree $bundle) } "$bundle/manifest.json"
$global:ReleaseFixture = @{ work = $work; commit = $commit; record = $null; deployed = $false; pushes = 0; edits = 0; version = $Version; site = $site }
function global:gh {
    $arguments = @($args); $global:LASTEXITCODE = 0
    if ($arguments[0] -eq 'api') {
        if (($arguments -join ' ') -match '/commits/') { $global:ReleaseFixture.commit; return }
        if (($arguments -join ' ') -match '/tags\?') { '[[{"name":"v1.1.2"},{"name":"v' + $global:ReleaseFixture.version + '"}]]'; return }
        if (($arguments -join ' ') -match '/releases\?') {
            if ($null -eq $global:ReleaseFixture.record) { '[[]]' }
            else { '[[' + ($global:ReleaseFixture.record | ConvertTo-Json -Depth 10 -Compress) + ']]' }
            return
        }
    }
    if ($arguments[0] -eq 'release') {
        switch ($arguments[1]) {
            'create' { $global:ReleaseFixture.record = @{ tag_name = $arguments[2]; draft = $true; assets = @() }; return }
            'upload' {
                Copy-Item $arguments[3] "$($global:ReleaseFixture.work)/remote-bundle.zip"
                $global:ReleaseFixture.record.assets = @(@{ name = 'release-bundle.zip' }); return
            }
            'download' {
                $dest = $arguments[[Array]::IndexOf($arguments, '--dir') + 1]
                Copy-Item "$($global:ReleaseFixture.work)/remote-bundle.zip" (Join-Path $dest 'release-bundle.zip'); return
            }
            'edit' {
                $index = [Array]::IndexOf($arguments, '--notes-file')
                if ($index -lt 0 -or $arguments -contains '--notes') { throw 'Completion must use --notes-file.' }
                $notesPath = Assert-ChildPath (Join-Path (Get-Repo) 'artifacts') $arguments[$index + 1]
                $global:ReleaseFixture.record.body = Get-Content -LiteralPath $notesPath -Raw
                $global:ReleaseFixture.edits++
                $global:ReleaseFixture.record.draft = $false; return
            }
        }
    }
    throw "Unexpected gh operation in fixture: $($arguments -join ' ')"
}
function global:dotnet {
    if ($args[0] -ne 'nuget' -or $args[1] -ne 'push') { throw 'No build is allowed during recovery.' }
    $global:ReleaseFixture.pushes++
    if ($global:ReleaseFixture.denyPush) {
        'error: Response status code does not indicate success: 403 (Forbidden).'
        $global:LASTEXITCODE = 1
        return
    }
    Copy-Item $args[2] "$($global:ReleaseFixture.work)/nuget.nupkg"
    # Service accepted the bytes, but the caller lost the response.
    $global:LASTEXITCODE = 1
}
function global:Invoke-WebRequest {
    param($Uri, $OutFile, $TimeoutSec)
    if ($Uri -like 'https://api.nuget.org/*') {
        $source = "$($global:ReleaseFixture.work)/nuget.nupkg"
        if (!(Test-Path $source)) {
            $exception = [Net.Http.HttpRequestException]::new('Not found')
            # Get-RemoteFile reads Response.StatusCode; use a PowerShell exception with that property.
            $exception | Add-Member -NotePropertyName Response -NotePropertyValue ([pscustomobject]@{ StatusCode = 404 })
            throw $exception
        }
        Copy-Item $source $OutFile; return
    }
    if (!$global:ReleaseFixture.deployed) { throw 'Simulated Pages deployment unavailable.' }
    $config = Get-SiteConfig
    $relative = ([uri]$Uri).AbsolutePath.Substring($config.basePath.Length)
    $source = Join-Path "$($global:ReleaseFixture.work)/archive" $relative
    if (!(Test-Path $source)) { throw "Missing fake online target: $relative" }
    Copy-Item $source $OutFile
}
$previousKey = $env:NUGET_API_KEY
try {
    $env:NUGET_API_KEY = 'fixture-not-a-real-secret'
    & "$PSScriptRoot/Publish-Release.ps1" -Stage Archive -Bundle $bundle
    $global:ReleaseFixture.denyPush = $true
    $denied = $false
    try { & "$PSScriptRoot/Publish-Release.ps1" -Stage NuGet -Bundle $bundle } catch {
        if ($_.Exception.Message -notmatch 'credentials or package permissions') { throw }
        $denied = $true
    }
    if (!$denied -or (Test-Path "$work/nuget.nupkg")) { throw 'Authorization failure was not reported before publication.' }
    $global:ReleaseFixture.denyPush = $false
    $global:ReleaseFixture.pushes = 0
    & "$PSScriptRoot/Publish-Release.ps1" -Stage NuGet -Bundle $bundle
    if ($global:ReleaseFixture.pushes -ne 1) { throw 'Expected exactly one package upload.' }
    $failed = $false
    try { & "$PSScriptRoot/Test-PublishedRelease.ps1" -Bundle $bundle } catch {
        if ($_.Exception.Message -notmatch 'Simulated Pages') { throw }
        $failed = $true
    }
    if (!$failed -or !$global:ReleaseFixture.record.draft) { throw 'Partial deployment incorrectly completed.' }
    $auditFailed = $false
    try { & "$PSScriptRoot/Test-ReleaseStatus.ps1" } catch {
        if ($_.Exception.Message -notmatch 'still a draft') { throw }
        $auditFailed = $true
    }
    if (!$auditFailed) { throw 'Audit did not detect the incomplete release.' }
    Remove-Item -LiteralPath (Assert-ChildPath $work $bundle) -Recurse -Force
    & "$PSScriptRoot/Resume-Release.ps1" -Tag "v$Version" -Output "$work/restored"
    $restored = Join-Path $work 'restored'
    & "$PSScriptRoot/Publish-Release.ps1" -Stage NuGet -Bundle $restored
    if ($global:ReleaseFixture.pushes -ne 1) { throw 'Recovery republished the package.' }
    $archive = Join-Path $work 'archive'
    & "$PSScriptRoot/Merge-DocumentationArchive.ps1" -Archive $archive -Site (Join-Path (Get-Repo) 'artifacts/restored-site')
    $global:ReleaseFixture.deployed = $true
    & "$PSScriptRoot/Test-PublishedRelease.ps1" -Bundle $restored
    & "$PSScriptRoot/Merge-DocumentationArchive.ps1" -Archive $archive -Complete -Version $Version
    & "$PSScriptRoot/Publish-Release.ps1" -Stage Complete -Bundle $restored
    $body = $global:ReleaseFixture.record.body
    $config = Get-SiteConfig
    $documentation = "$($config.origin)$($config.basePath)v$Version/"
    foreach ($required in @('**Before upgrading:**', 'breaking changes', 'rebuild',
        "https://github.com/$($config.repository)/blob/v$Version/CHANGELOG.md", $documentation)) {
        if (!$body.Contains($required)) { throw "Release body missing: $required" }
    }
    if ($body -match '/blob/main/|/latest/|/dev/' -or ($body -split "`n").Count -lt 6) { throw 'Release body must contain fixed links and upgrade guidance.' }
    if ($Version -eq '1.3.0') {
        foreach ($language in @('en','zh-CN')) {
            if (!$body.Contains("${documentation}docs/$language/upgrading.html")) { throw "Missing $language upgrade guide." }
        }
    } elseif ($body.Contains('upgrading.html')) { throw 'Historical release linked an unavailable upgrade guide.' }
    # A later user edit to a public Release must also survive recovery.
    $global:ReleaseFixture.record.body += "`nMaintainer annotation."
    $publishedBody = $global:ReleaseFixture.record.body
    & "$PSScriptRoot/Publish-Release.ps1" -Stage Complete -Bundle $restored
    if ($global:ReleaseFixture.edits -ne 1 -or $global:ReleaseFixture.record.body -cne $publishedBody) { throw 'Repeat completion rewrote a public Release.' }
    if ($global:ReleaseFixture.record.draft) { throw 'Recovery did not complete.' }
    & "$PSScriptRoot/Test-ReleaseStatus.ps1"
    $savedCommit = $global:ReleaseFixture.commit
    $global:ReleaseFixture.commit = 'b' * 40
    $conflict = $false
    try { & "$PSScriptRoot/Publish-Release.ps1" -Stage NuGet -Bundle $restored } catch {
        if ($_.Exception.Message -notmatch 'Remote tag differs') { throw }
        $conflict = $true
    }
    $global:ReleaseFixture.commit = $savedCommit
    if (!$conflict) { throw 'Changed tag was accepted.' }
    Write-Json @{ version = $Version; passed = $true; simulation = $true; scenarios = @('NuGet accepted but response lost','Pages failed after package publication','original transient bundle deleted','restore without build','no duplicate package push','versioned release notes','repeat completion preserves public body') } "$work/result.json"
    Write-Host 'PASS: offline end-to-end recovery; no real service or package was changed.'
} finally {
    $env:NUGET_API_KEY = $previousKey
    Remove-Item Function:global:gh,Function:global:dotnet,Function:global:Invoke-WebRequest
    Remove-Variable ReleaseFixture -Scope Global
}
