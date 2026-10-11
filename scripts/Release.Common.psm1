Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:Repo = if ($env:DOCS_SOURCE_ROOT) { $env:DOCS_SOURCE_ROOT } else { Split-Path $PSScriptRoot -Parent }
function Get-Repo { $script:Repo }
function Resolve-FullPath([string]$Path) { $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path) }
function Get-SiteConfig { Get-Content (Join-Path $script:Repo 'docs/site.json') -Raw | ConvertFrom-Json }
function Invoke-Checked([string]$Command, [string[]]$Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command failed (exit $LASTEXITCODE). Review the preceding output." }
}
function Write-Json($Value, [string]$Path) {
    [void][IO.Directory]::CreateDirectory((Split-Path (Resolve-FullPath $Path) -Parent))
    $Value | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $Path -Encoding utf8NoBOM
}
function Get-Version([string]$Value) {
    if ($Value -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$') { throw "Invalid version: $Value" }
    $parsed = [System.Management.Automation.SemanticVersion]::Parse($Value)
    if ($parsed -lt [System.Management.Automation.SemanticVersion]::Parse((Get-SiteConfig).minimumVersion)) { throw 'Version predates documentation retention.' }
    $parsed
}
function Get-Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Get-Tree([string]$Path) {
    $root = Resolve-FullPath $Path
    $result = [ordered]@{}
    foreach ($file in Get-ChildItem -LiteralPath $root -File -Recurse | Sort-Object FullName) {
        if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked file: $($file.FullName)" }
        $result[[IO.Path]::GetRelativePath($root, $file.FullName).Replace('\', '/')] = Get-Hash $file.FullName
    }
    $result
}
function Assert-Tree($Expected, [string]$Path) {
    $actual = Get-Tree $Path
    if ($actual.Count -ne $Expected.Count) { throw "File count mismatch: $Path" }
    foreach ($key in $Expected.Keys) {
        if (!$actual.Contains($key) -or $actual[$key] -cne $Expected[$key]) { throw "Content mismatch: $Path/$key" }
    }
}
function Assert-ChildPath([string]$Root, [string]$Path) {
    $rootPath = (Resolve-FullPath $Root).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $full = Resolve-FullPath $Path
    if (!$full.StartsWith($rootPath, [StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe output path: $full" }
    $cursor = $full
    while ($cursor -and $cursor.Length -ge $rootPath.TrimEnd([IO.Path]::DirectorySeparatorChar).Length) {
        if ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Linked output path: $cursor" }
        $cursor = Split-Path $cursor -Parent
    }
    $full
}
function Reset-Output([string]$Path) {
    $full = Assert-ChildPath (Join-Path $script:Repo 'artifacts') $Path
    if (Test-Path -LiteralPath $full) { Remove-Item -LiteralPath $full -Recurse -Force }
    [void][IO.Directory]::CreateDirectory($full)
    $full
}
function Get-Commit {
    $value = & git -C $script:Repo rev-parse HEAD
    if ($LASTEXITCODE) { throw 'Cannot resolve HEAD.' }
    $value.Trim()
}
function Get-PackageVersion {
    $value = & dotnet msbuild (Join-Path $script:Repo 'src/Fizzy.ImageViewer/Fizzy.ImageViewer.csproj') -nologo -getProperty:PackageVersion
    if ($LASTEXITCODE) { throw 'Cannot resolve PackageVersion.' }
    ($value -join '').Trim()
}
function Assert-ReleaseIdentity([string]$Version) {
    [void](Get-Version $Version)
    if ((Get-PackageVersion) -cne $Version) { throw 'Project PackageVersion differs from release version.' }
    $status = & git -C $script:Repo status --porcelain
    if ($LASTEXITCODE -or $status) { throw 'Release source checkout must be clean.' }
    $tagCommit = & git -C $script:Repo rev-parse "refs/tags/v${Version}^{commit}"
    if ($LASTEXITCODE -or $tagCommit -cne (Get-Commit)) { throw 'Release tag must resolve to HEAD.' }
    & git -C $script:Repo merge-base --is-ancestor HEAD origin/main
    if ($LASTEXITCODE) { throw 'Release commit must be reachable from origin/main.' }
}
function Expand-SafeZip([string]$Zip, [string]$Destination) {
    $root = Resolve-FullPath $Destination
    [void][IO.Directory]::CreateDirectory($root)
    $archive = [IO.Compression.ZipFile]::OpenRead((Resolve-FullPath $Zip))
    try {
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in $archive.Entries) {
            if ($entry.FullName.EndsWith('/')) { continue }
            $target = Assert-ChildPath $root (Join-Path $root $entry.FullName)
            if (!$seen.Add($target)) { throw 'Duplicate archive entry.' }
            [void][IO.Directory]::CreateDirectory((Split-Path $target -Parent))
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $false)
        }
    } finally { $archive.Dispose() }
}
function Get-PackagePayload([string]$Path) {
    $archive = [IO.Compression.ZipFile]::OpenRead((Resolve-FullPath $Path))
    try {
        $entries = @{}
        foreach ($entry in $archive.Entries) {
            if ($entry.FullName -eq '.signature.p7s' -or $entry.FullName.EndsWith('/')) { continue }
            if ($entries.ContainsKey($entry.FullName)) { throw 'Duplicate package entry.' }
            $stream = $entry.Open()
            try { $entries[$entry.FullName] = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() }
            finally { $stream.Dispose() }
        }
        $entries
    } finally { $archive.Dispose() }
}
function Assert-PackageEqual([string]$Expected, [string]$Actual) {
    $left = Get-PackagePayload $Expected; $right = Get-PackagePayload $Actual
    if ($left.Count -ne $right.Count) { throw 'Published package payload count differs.' }
    foreach ($name in $left.Keys) { if ($left[$name] -cne $right[$name]) { throw "Published package differs: $name" } }
}
function Get-RemoteFile([string]$Uri, [string]$Path, [switch]$AllowMissing) {
    for ($attempt = 0; $attempt -lt 4; $attempt++) {
        try { Invoke-WebRequest -Uri $Uri -OutFile $Path -TimeoutSec 60; return $true }
        catch {
            $response = if ($_.Exception.PSObject.Properties['Response']) { $_.Exception.Response } else { $null }
            $code = if ($null -ne $response) { [int]$response.StatusCode } else { 0 }
            if ($AllowMissing -and $code -eq 404) { return $false }
            if ($code -in @(400,401,403,404) -or $attempt -eq 3) { throw }
            Start-Sleep -Seconds (2 * ($attempt + 1))
        }
    }
}
Export-ModuleMember -Function *
