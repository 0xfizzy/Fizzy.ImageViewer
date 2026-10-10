[CmdletBinding()]
param([Parameter(Mandatory)][string]$Package, [Parameter(Mandatory)][string]$Version, [string]$DocumentationInput)
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Release.Common.psm1" -Force
$zip = [IO.Compression.ZipFile]::OpenRead((Resolve-FullPath $Package))
try {
    function Read-Entry($Entry) {
        if ($null -eq $Entry) { throw 'Missing package entry.' }
        $reader = [IO.StreamReader]::new($Entry.Open())
        try { $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    $specs = @($zip.Entries | Where-Object FullName -Like '*.nuspec')
    if ($specs.Count -ne 1) { throw 'Expected one nuspec.' }
    [xml]$spec = Read-Entry $specs[0]
    if ($spec.package.metadata.id -cne (Get-SiteConfig).packageId -or $spec.package.metadata.version -cne $Version) { throw 'Package identity mismatch.' }
    if ($spec.package.metadata.license.InnerText -ne 'MIT') { throw 'MIT license metadata required.' }
    $readme = Read-Entry ($zip.GetEntry('README.md'))
    $config = Get-SiteConfig
    if (!$readme.Contains("$($config.origin)$($config.basePath)v$Version/")) { throw 'README must link to the fixed release documentation.' }
    $dlls = @($zip.Entries | Where-Object FullName -Match '^lib/[^/]+/Fizzy\.ImageViewer\.dll$')
    if (!$dlls.Count) { throw 'No library targets in package.' }
    foreach ($dll in $dlls) {
        $xmlEntry = $zip.GetEntry([IO.Path]::ChangeExtension($dll.FullName, '.xml').Replace('\','/'))
        [xml]$contracts = Read-Entry $xmlEntry
        foreach ($id in @('T:Fizzy.ImageViewer.IViewer','T:Fizzy.ImageViewer.Frames.IFramePixelSource','T:Fizzy.ImageViewer.Frames.ImageFrame')) {
            if (!@($contracts.doc.members.member | Where-Object name -CEQ $id).Count) { throw "Missing packaged contract: $id" }
        }
        if ($DocumentationInput) {
            foreach ($entry in @($dll, $xmlEntry)) {
                $stream = $entry.Open()
                try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() }
                finally { $stream.Dispose() }
                if ($hash -cne (Get-Hash (Join-Path $DocumentationInput $entry.Name))) { throw "API input/package mismatch: $($entry.Name)" }
            }
        }
    }
} finally { $zip.Dispose() }
Write-Host 'PASS: package identity, XML, README, license and API inputs.'
