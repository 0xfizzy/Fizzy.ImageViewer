[CmdletBinding()]
param([string[]]$AcceptTranslation = @())
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $repo 'docs/translations.json'
$pairs = [ordered]@{ 'README.md' = 'README.zh-CN.md' }
foreach ($file in Get-ChildItem (Join-Path $repo 'docs') -File -Filter '*.md' | Where-Object Name -NE 'nuget-readme.md' | Sort-Object Name) {
    $pairs["docs/$($file.Name)"] = "docs/zh-CN/$($file.Name)"
}
function Get-SourceHash([string]$Path) {
    $normalized = [IO.File]::ReadAllText($Path).Replace("`r`n", "`n")
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($normalized))).ToLowerInvariant()
}
$manifest = if (Test-Path -LiteralPath $manifestPath) { Get-Content $manifestPath -Raw | ConvertFrom-Json -AsHashtable } else { @{} }
foreach ($source in $AcceptTranslation) {
    if (!$pairs.Contains($source)) { throw "Not an English documentation source: $source" }
    if (!(Test-Path -LiteralPath (Join-Path $repo $pairs[$source]))) { throw "Missing translation: $($pairs[$source])" }
    $manifest[$source] = Get-SourceHash (Join-Path $repo $source)
}
if ($AcceptTranslation.Count) {
    $sorted = [ordered]@{}
    foreach ($key in $manifest.Keys | Sort-Object) { if ($pairs.Contains($key)) { $sorted[$key] = $manifest[$key] } }
    $sorted | ConvertTo-Json | Set-Content $manifestPath -Encoding utf8
}
$failures = [Collections.Generic.List[string]]::new()
foreach ($source in $pairs.Keys) {
    if (!(Test-Path -LiteralPath (Join-Path $repo $pairs[$source]))) { $failures.Add("Missing translation: $($pairs[$source])"); continue }
    if ($manifest[$source] -ne (Get-SourceHash (Join-Path $repo $source))) { $failures.Add("Translation needs review: $source -> $($pairs[$source])") }
}
foreach ($translation in Get-ChildItem (Join-Path $repo 'docs/zh-CN') -Filter '*.md' -File -ErrorAction SilentlyContinue) {
    if (!(Test-Path -LiteralPath (Join-Path $repo "docs/$($translation.Name)"))) { $failures.Add("Translation has no source: $($translation.Name)") }
}
foreach ($source in $pairs.Keys) {
    $translation = $pairs[$source]
    foreach ($pair in @(@($source, $translation), @($translation, $source))) {
        $path = Join-Path $repo $pair[0]
        if (!(Test-Path $path)) { continue }
        $counterpart = [IO.Path]::GetRelativePath((Split-Path $path -Parent), (Join-Path $repo $pair[1])).Replace('\','/')
        if (!(Get-Content $path -Raw).Contains("]($counterpart)")) { $failures.Add("Missing language link in $($pair[0]): $counterpart") }
    }
    if ($source.StartsWith('docs/')) {
        foreach ($relative in @($source, $translation)) {
            $toc = Join-Path (Split-Path (Join-Path $repo $relative) -Parent) 'toc.yml'
            $name = Split-Path $relative -Leaf
            $pattern = '(?m)^\s*href:\s*[''"]?' + [regex]::Escape($name) + '[''"]?\s*$'
            if (!(Test-Path $toc) -or (Get-Content $toc -Raw) -notmatch $pattern) { $failures.Add("Guide missing from navigation: $relative") }
        }
    }
}
# Check repository-local Markdown file targets before DocFX checks rendered links and xrefs.
foreach ($relative in @($pairs.Keys) + @($pairs.Values)) {
    $path = Join-Path $repo $relative
    if (!(Test-Path -LiteralPath $path)) { continue }
    $body = [IO.File]::ReadAllText($path)
    $body = [regex]::Replace($body, '(?ms)^```.*?^```[^\r\n]*', '')
    foreach ($match in [regex]::Matches($body, '\]\((?<target>[^\s)]+)(?:\s+"[^"]*")?\)')) {
        $target = $match.Groups['target'].Value.Trim('<', '>')
        if ($target -match '^(?:[a-zA-Z][a-zA-Z0-9+.-]*:|#|/)') { continue }
        $target = [Uri]::UnescapeDataString(($target -split '[#?]', 2)[0])
        if (!$target -or $target -notmatch '\.(md|cs|csproj|json|ps1|yml)$') { continue }
        if (!(Test-Path -LiteralPath (Join-Path (Split-Path $path -Parent) $target))) { $failures.Add("Broken link in ${relative}: $target") }
    }
}
if ($failures.Count) { throw ($failures -join "`n") }
Write-Host "PASS: $($pairs.Count) English/Chinese page pairs, source revisions and local file links."
