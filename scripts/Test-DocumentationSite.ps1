[CmdletBinding()]
param([string]$Site)
$ErrorActionPreference = 'Stop'
if (!$Site) { $Site = [IO.Path]::GetFullPath((Join-Path $(if ($env:DOCS_SOURCE_ROOT) { $env:DOCS_SOURCE_ROOT } else { Split-Path -Parent $PSScriptRoot }) 'artifacts/docs/site')) }
$Site = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Site)
if (!(Test-Path -LiteralPath (Join-Path $site 'index.html'))) { throw 'Build the documentation site before checking it.' }
$pages = @{}
foreach ($file in Get-ChildItem $site -Recurse -Filter '*.html') {
    $html = [IO.File]::ReadAllText($file.FullName)
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($match in [regex]::Matches($html, '\bid="([^"]*)"')) { [void]$ids.Add([Net.WebUtility]::HtmlDecode($match.Groups[1].Value)) }
    $links = @([regex]::Matches($html, '<(?:a|link|script|img)\b[^>]*\b(?:href|src)="([^"]*)"') | ForEach-Object { [Net.WebUtility]::HtmlDecode($_.Groups[1].Value) })
    $pages[$file.FullName] = @{ Ids = $ids; Links = $links }
}
$failures = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($path in $pages.Keys) {
    foreach ($link in $pages[$path].Links) {
        if (!$link -or $link -match '^(?:[a-zA-Z][a-zA-Z0-9+.-]*:|//)') { continue }
        $relative = [IO.Path]::GetRelativePath($site, $path).Replace('\', '/')
        $uri = [Uri]::new([Uri]::new("https://documentation.invalid/$relative"), $link)
        $target = [IO.Path]::GetFullPath((Join-Path $site ([Uri]::UnescapeDataString($uri.AbsolutePath).TrimStart('/'))))
        if (Test-Path -LiteralPath $target -PathType Container) { $target = Join-Path $target 'index.html' }
        if (!(Test-Path -LiteralPath $target)) { [void]$failures.Add("Missing site target: $target"); continue }
        $anchor = [Uri]::UnescapeDataString($uri.Fragment.TrimStart('#'))
        if ($anchor -and $pages.ContainsKey($target) -and !$pages[$target].Ids.Contains($anchor)) {
            [void]$failures.Add("Missing site anchor: ${target}#$anchor")
        }
    }
}
if ($failures.Count) { throw (($failures | Sort-Object) -join "`n") }
$inventory = @{}
foreach ($path in $pages.Keys) {
    $inventory[[IO.Path]::GetRelativePath($site, $path).Replace('\', '/')] = @($pages[$path].Ids)
}
$inventory | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $site 'pages.json') -Encoding utf8
Write-Host "PASS: $($pages.Count) rendered HTML pages, local links and anchors."
