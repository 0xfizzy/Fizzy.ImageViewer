# DocFX 2.78 emits links for every namespace segment, even parents with no types.
# Generate navigation-only parent pages from metadata so those links resolve locally.
$ErrorActionPreference = 'Stop'
$apiRoot = Join-Path $(if ($env:DOCS_SOURCE_ROOT) { $env:DOCS_SOURCE_ROOT } else { Split-Path -Parent $PSScriptRoot }) 'artifacts/docs/api'
$namespaces = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($file in Get-ChildItem $apiRoot -Filter '*.yml') {
    foreach ($match in [regex]::Matches([IO.File]::ReadAllText($file.FullName), '(?m)^  namespace: ([A-Za-z_][A-Za-z0-9_.]*)\r?$')) {
        $name = $match.Groups[1].Value
        while ($name) {
            [void]$namespaces.Add($name)
            $dot = $name.LastIndexOf('.')
            $name = if ($dot -lt 0) { '' } else { $name.Substring(0, $dot) }
        }
    }
}
foreach ($name in $namespaces | Sort-Object) {
    $path = Join-Path $apiRoot "$name.yml"
    if (Test-Path -LiteralPath $path) { continue }
    $children = @($namespaces | Where-Object { $_.StartsWith("$name.", [StringComparison]::Ordinal) -and $_.LastIndexOf('.') -eq $name.Length } | Sort-Object)
    $lines = @('### YamlMime:ManagedReference', 'items:', "- uid: $name", "  id: $name", "  name: $name", "  fullName: $name", '  type: Namespace', '  children:')
    $lines += @($children | ForEach-Object { "  - $_" })
    $lines += 'references:'
    foreach ($child in $children) { $lines += @("- uid: $child", "  name: $child", "  fullName: $child", "  href: $child.html") }
    $lines | Set-Content -LiteralPath $path -Encoding utf8
}

$index = @('# API reference (English)', '', 'Select a namespace to browse its public types and members.', '')
$index += @($namespaces | Sort-Object | ForEach-Object { "- [$($_)]($($_).yml)" })
$index | Set-Content (Join-Path $apiRoot 'index.md') -Encoding utf8
