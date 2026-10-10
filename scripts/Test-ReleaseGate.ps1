[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Release.Common.psm1" -Force
$root = Reset-Output (Join-Path (Get-Repo) 'artifacts/release-gate')
$archive = Join-Path $root 'archive'
# PowerShell location can differ from Environment.CurrentDirectory (for example in a standalone checkout).
Push-Location $root
try {
    Set-Content location-proof.txt 'current PowerShell location'
    $localTree = Get-Tree '.'
    if (!$localTree.Contains('location-proof.txt')) { throw 'Relative paths ignored the PowerShell location.' }
} finally { Pop-Location }
function Expect-Failure([scriptblock]$Action, [string]$Pattern) {
    try { & $Action } catch { if ($_.Exception.Message -notmatch $Pattern) { throw }; return }
    throw "Expected failure: $Pattern"
}
function New-Site([string]$Version, [string]$Channel = 'Release') {
    $directory = Join-Path $root "$Version-$Channel"
    [void][IO.Directory]::CreateDirectory($directory)
    Set-Content "$directory/index.html" "<html>$Version</html>"
    Write-Json @{ version = $Version; channel = $Channel; directory = $(if ($Channel -eq 'Release') { "v$Version" } else { 'dev' }); commit = ('a' * 40); basePath = (Get-SiteConfig).basePath } "$directory/identity.json"
    $directory
}
$first = New-Site '1.2.0'
$second = New-Site '1.2.1'
$preview = New-Site '1.3.0-rc.1'
& "$PSScriptRoot/Merge-DocumentationArchive.ps1" -Archive $archive -Site $first
$snapshot = Get-Tree "$archive/v1.2.0"
& "$PSScriptRoot/Merge-DocumentationArchive.ps1" -Archive $archive -Site $first
Assert-Tree $snapshot "$archive/v1.2.0"
if ((Get-Content "$archive/latest/index.html" -Raw).Contains('v1.2.0/')) { throw 'Unverified version promoted.' }
& "$PSScriptRoot/Merge-DocumentationArchive.ps1" -Archive $archive -Complete -Version '1.2.0'
& "$PSScriptRoot/Merge-DocumentationArchive.ps1" -Archive $archive -Site $second
& "$PSScriptRoot/Merge-DocumentationArchive.ps1" -Archive $archive -Complete -Version '1.2.1'
& "$PSScriptRoot/Merge-DocumentationArchive.ps1" -Archive $archive -Site $preview
& "$PSScriptRoot/Merge-DocumentationArchive.ps1" -Archive $archive -Complete -Version '1.3.0-rc.1'
& "$PSScriptRoot/Merge-DocumentationArchive.ps1" -Archive $archive -Complete -Version '1.2.0'
$dev = New-Site '1.3.0' 'Development'
& "$PSScriptRoot/Merge-DocumentationArchive.ps1" -Archive $archive -Site $dev
if (!(Get-Content "$archive/latest/index.html" -Raw).Contains('v1.2.1/')) { throw 'Latest stable regressed.' }
Assert-Tree $snapshot "$archive/v1.2.0"
Set-Content "$first/index.html" 'changed'
& "$PSScriptRoot/Merge-DocumentationArchive.ps1" -Archive $archive -Site $first
Assert-Tree (Get-Tree $first) "$archive/v1.2.0"
Expect-Failure { Get-Version '../1.2.0' } 'Invalid version'
Expect-Failure { Get-Version 'v1.2.0' } 'Invalid version'
Expect-Failure { Get-Version '1.1.2' } 'predates'
Expect-Failure { Assert-ChildPath $root (Split-Path $root -Parent) } 'Unsafe output'
Set-Content "$archive/v1.2.0/index.html" 'tampered'
Expect-Failure { & "$PSScriptRoot/Merge-DocumentationArchive.ps1" -Archive $archive -Site $dev } 'Content mismatch'

# Model a server-added NuGet signature and reject changed payloads without comparing zip bytes.
$payload = Join-Path $root 'payload'
[void][IO.Directory]::CreateDirectory($payload)
Set-Content "$payload/library.dll" 'original'
[IO.Compression.ZipFile]::CreateFromDirectory($payload, "$root/original.nupkg")
Set-Content "$payload/.signature.p7s" 'server signature'
[IO.Compression.ZipFile]::CreateFromDirectory($payload, "$root/signed.nupkg")
Assert-PackageEqual "$root/original.nupkg" "$root/signed.nupkg"
Set-Content "$payload/library.dll" 'conflict'
[IO.Compression.ZipFile]::CreateFromDirectory($payload, "$root/conflict.nupkg")
Expect-Failure { Assert-PackageEqual "$root/original.nupkg" "$root/conflict.nupkg" } 'differs'

# A retained zip can restore the exact site after all transient build output is gone.
[IO.Compression.ZipFile]::CreateFromDirectory($second, "$root/site.zip")
$tree = Get-Tree $second
Remove-Item -LiteralPath (Assert-ChildPath $root $second) -Recurse -Force
Expand-SafeZip "$root/site.zip" "$root/restored"
Assert-Tree $tree "$root/restored"
Push-Location $root
try {
    Expand-SafeZip 'site.zip' 'relative-restored'
    Assert-Tree $tree 'relative-restored'
    Assert-PackageEqual 'original.nupkg' 'signed.nupkg'
} finally { Pop-Location }
$unsafeZip = [IO.Compression.ZipFile]::Open("$root/unsafe.zip", [IO.Compression.ZipArchiveMode]::Create)
try { [void]$unsafeZip.CreateEntry('../outside.txt') } finally { $unsafeZip.Dispose() }
Expect-Failure { Expand-SafeZip "$root/unsafe.zip" "$root/safe-expansion" } 'Unsafe output'

# Exercise the real Git archive writer against a local bare remote, including Windows CRLF conversion.
$gitArchive = Join-Path $root 'git-archive'
$bare = Join-Path $root 'remote.git'
$clone = Join-Path $root 'roundtrip'
Invoke-Checked git @('init','--bare','--quiet',$bare)
Invoke-Checked git @('init','--initial-branch',(Get-SiteConfig).archiveBranch,'--quiet',$gitArchive)
Invoke-Checked git @('-C',$gitArchive,'config','core.autocrlf','true')
Invoke-Checked git @('-C',$gitArchive,'remote','add','origin',$bare)
& "$PSScriptRoot/Merge-DocumentationArchive.ps1" -Archive $gitArchive -Site "$root/restored"
& "$PSScriptRoot/Save-DocumentationArchive.ps1" -Archive $gitArchive -Message 'test: preserve archived bytes across git checkouts'
Invoke-Checked git @('clone','--quiet','--branch',(Get-SiteConfig).archiveBranch,'--config','core.autocrlf=true',$bare,$clone)
Assert-Tree $tree "$clone/v1.2.1"
Write-Host 'PASS: retained versions, idempotency, conflicts, latest ordering, development merge, signed package payloads and artifact-independent restore.'
