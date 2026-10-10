[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Release.Common.psm1" -Force
$directory = Join-Path (Get-Repo) 'artifacts/actionlint'
[void][IO.Directory]::CreateDirectory($directory)
$zip = Join-Path $directory 'actionlint.zip'
$expected = '7f12f1801bca3d480d67aaf7774f4c2a6359a3ca8eebe382c95c10c9704aa731'
if (!(Test-Path $zip) -or (Get-Hash $zip) -cne $expected) {
    $null = Get-RemoteFile 'https://github.com/rhysd/actionlint/releases/download/v1.7.7/actionlint_1.7.7_windows_amd64.zip' $zip
}
if ((Get-Hash $zip) -cne $expected) { throw 'actionlint download checksum mismatch.' }
$binaryDir = Reset-Output (Join-Path $directory 'bin')
Expand-SafeZip $zip $binaryDir
Push-Location (Get-Repo)
try { Invoke-Checked (Join-Path $binaryDir 'actionlint.exe') @() }
finally { Pop-Location }
Write-Host 'PASS: GitHub workflow syntax, expressions and action inputs.'
Invoke-Checked node @((Join-Path $PSScriptRoot 'Test-PagesDeployment.cjs'))
