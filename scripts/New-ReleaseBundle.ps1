[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Release.Common.psm1" -Force
$repo = Get-Repo
$report = Get-Content "$repo/artifacts/release/validation.json" -Raw | ConvertFrom-Json -AsHashtable
if (!$report.passed -or !$report.release) { throw 'A successful release-mode validation is required.' }
Assert-ReleaseIdentity $report.version
Assert-Tree $report.siteFiles "$repo/artifacts/docs/site"
$package = @(Get-ChildItem "$repo/artifacts/release/packages" -Filter '*.nupkg')
if ($package.Count -ne 1 -or (Get-Hash $package[0].FullName) -cne $report.packageHash) { throw 'Package changed after validation.' }
$output = Reset-Output "$repo/artifacts/bundle"
Copy-Item $package[0].FullName $output
Copy-Item "$repo/artifacts/release/validation.json" $output
Copy-Item "$repo/artifacts/docs/api-contracts.csv" $output
Copy-Item "$repo/artifacts/docs/browser/result.json" "$output/browser-validation.json"
[IO.Compression.ZipFile]::CreateFromDirectory("$repo/artifacts/docs/site", "$output/documentation.zip")
$identity = Get-Content "$repo/artifacts/docs/site/identity.json" -Raw | ConvertFrom-Json
Write-Json @{ schema = 1; version = $report.version; tag = "v$($report.version)"; commit = $report.commit; tools = @{ sdk = $identity.sdk; docfx = $identity.docfx; node = (& node --version); playwright = '1.56.1'; actionlint = '1.7.7' }; files = (Get-Tree $output) } "$output/manifest.json"
Write-Host "PASS: release bundle at $output"
