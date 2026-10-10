[CmdletBinding()]
param([Parameter(Mandatory)][string]$Bundle, [switch]$RequireCompleted)
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Release.Common.psm1" -Force
Import-Module "$PSScriptRoot/Release.Remote.psm1" -Force
$manifest = Assert-Bundle $Bundle
if (!(Test-NuGetPayload $Bundle)) { throw 'NuGet is missing.' }
$remote = Get-ArchivedBundle $manifest.tag
Assert-Tree (Get-Tree $Bundle) $remote
$config = Get-SiteConfig
$root = "$($config.origin)$($config.basePath)"
$report = Get-Content (Join-Path $Bundle 'validation.json') -Raw | ConvertFrom-Json -AsHashtable
$siteArchive = [IO.Compression.ZipFile]::OpenRead((Resolve-FullPath (Join-Path $Bundle 'documentation.zip')))
try {
    $reader = [IO.StreamReader]::new($siteArchive.GetEntry('identity.json').Open())
    try { $identity = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
} finally { $siteArchive.Dispose() }
$work = Reset-Output (Join-Path (Get-Repo) 'artifacts/online-check')
# Check every immutable site file; CDN propagation is bounded and never treated as success.
foreach ($entry in $report.siteFiles.GetEnumerator()) {
    $target = Assert-ChildPath $work (Join-Path $work $entry.Key)
    [void][IO.Directory]::CreateDirectory((Split-Path $target -Parent))
    $url = "$root$($identity.directory)/$($entry.Key)"
    $matched = $false
    for ($attempt = 0; $attempt -lt 3; $attempt++) {
        $exists = Get-RemoteFile $url $target -AllowMissing
        if ($exists -and (Get-Hash $target) -ceq $entry.Value) { $matched = $true; break }
        Start-Sleep -Seconds 5
    }
    if (!$matched) { throw "Published file differs: $url" }
}
if ($RequireCompleted) {
    $null = Get-RemoteFile "${root}versions.json" "$work/versions.json"
    $index = Get-Content "$work/versions.json" -Raw | ConvertFrom-Json
    $entry = @($index.versions | Where-Object version -CEQ $manifest.version)
    if ($entry.Count -ne 1 -or !$entry[0].completed -or $entry[0].commit -cne $manifest.commit) { throw 'Online version index is not complete.' }
    $stable = @($index.versions | Where-Object { $_.completed -and $_.version -notmatch '-' } | Sort-Object { [System.Management.Automation.SemanticVersion]::Parse($_.version) } -Descending)
    if ($stable.Count) {
        foreach ($path in @('index.html','latest/index.html')) {
            $target = Join-Path $work ($path.Replace('/','-'))
            $null = Get-RemoteFile "$root$path" $target
            if (!(Get-Content $target -Raw).Contains("v$($stable[0].version)/")) { throw 'Latest stable entry is incorrect.' }
        }
    }
}
Write-Host 'PASS: NuGet, durable bundle and immutable online files agree.'
