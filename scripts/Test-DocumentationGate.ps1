[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path $repo ('artifacts/documentation-gate-' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $fixture -Force)
$project = Join-Path $fixture 'Fizzy.ImageViewer.DocFixture.csproj'
$source = Join-Path $fixture 'Contract.cs'
# Inherit the real repository Directory.Build.props and its documentation settings.
# This fixture has no package/project dependencies or shared build outputs.
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
</Project>
'@ | Set-Content -LiteralPath $project -Encoding utf8
'public sealed class Contract { }' | Set-Content -LiteralPath $source -Encoding utf8

function Invoke-FixtureBuild([string[]]$Options) {
    $output = & dotnet build $project -c Release --nologo --verbosity minimal @Options 2>&1
    $exitCode = $LASTEXITCODE
    [pscustomobject]@{ ExitCode = $exitCode; Output = ($output -join "`n") }
}

$ordinary = Invoke-FixtureBuild @('-p:DocumentationStrict=false')
if ($ordinary.ExitCode -ne 0 -or $ordinary.Output -notmatch '\bCS1591\b') {
    throw "Expected a successful ordinary build with CS1591. Fixture: $fixture`n$($ordinary.Output)"
}
$strict = Invoke-FixtureBuild @('-p:DocumentationStrict=true', '--no-incremental')
if ($strict.ExitCode -eq 0 -or $strict.Output -notmatch '\berror CS1591\b') {
    throw "Strict rebuild did not reject the previously compiled undocumented API with CS1591. Fixture: $fixture`n$($strict.Output)"
}
@'
/// <summary>A public contract used to verify the repository documentation compiler gate.</summary>
public sealed class Contract { }
'@ | Set-Content -LiteralPath $source -Encoding utf8
$documented = Invoke-FixtureBuild @('-p:DocumentationStrict=true', '--no-incremental')
if ($documented.ExitCode -ne 0 -or $documented.Output -match '\bCS1591\b') {
    throw "Documented API did not pass the strict rebuild. Fixture: $fixture`n$($documented.Output)"
}
Write-Host 'PASS: ordinary compilation reports CS1591; strict nonincremental compilation rejects it; valid XML passes.'

foreach ($case in @(
    @{ Code = 'CS1574'; Source = '/// <summary>See <see cref="MissingContract"/>.</summary>' + "`npublic sealed class Contract { }" },
    @{ Code = 'CS1570'; Source = '/// <summary>Unclosed <see></summary>' + "`npublic sealed class Contract { }" },
    @{ Code = 'CS1572'; Source = '/// <summary>Fixture.</summary>' + "`npublic sealed class Contract {`n/// <summary>Action.</summary>`n/// <param name=`"missing`">Unknown parameter.</param>`npublic void Run() { }`n}" }
)) {
    Set-Content -LiteralPath $source -Value $case.Source -Encoding utf8
    $result = Invoke-FixtureBuild @('-p:DocumentationStrict=true', '--no-incremental')
    if ($result.ExitCode -eq 0 -or $result.Output -notmatch $case.Code) { throw "Strict XML failed to reject $($case.Code): $($result.Output)" }
}
Write-Host 'PASS: invalid references, malformed XML and nonexistent parameter descriptions fail.'

# Exercise the actual translation checker in its own miniature repository, so its
# PSScriptRoot-based paths and manifest writes cannot affect the real guides.
$translationRoot = Join-Path $fixture 'translation-repo'
[void](New-Item -ItemType Directory -Force -Path (Join-Path $translationRoot 'scripts'), (Join-Path $translationRoot 'docs/zh-CN'))
$checker = Join-Path $translationRoot 'scripts/Test-Documentation.ps1'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Test-Documentation.ps1') -Destination $checker
$englishGuide = Join-Path $translationRoot 'docs/guide.md'
$chineseGuide = Join-Path $translationRoot 'docs/zh-CN/guide.md'
[IO.File]::WriteAllText((Join-Path $translationRoot 'README.md'), "# Example`n`n[中文](README.zh-CN.md)`n[Guide](docs/guide.md)`n")
[IO.File]::WriteAllText((Join-Path $translationRoot 'README.zh-CN.md'), "# 示例`n`n[English](README.md)`n[指南](docs/zh-CN/guide.md)`n")
[IO.File]::WriteAllText($englishGuide, "# Guide`n`n[中文](zh-CN/guide.md)`nInitial behavior.`n")
[IO.File]::WriteAllText($chineseGuide, "# 指南`n`n[English](../guide.md)`n初始行为。`n")
Set-Content (Join-Path $translationRoot 'docs/toc.yml') "- name: Guide`n  href: guide.md"
Set-Content (Join-Path $translationRoot 'docs/zh-CN/toc.yml') "- name: 指南`n  href: guide.md"

function Assert-TranslationCheck([string[]]$Accept = @(), [string]$ExpectedError = '') {
    $actualError = $null
    try { & $checker -AcceptTranslation $Accept 6>$null }
    catch { $actualError = $_.Exception.Message }
    if ($ExpectedError) {
        if ($actualError -cne $ExpectedError) {
            throw "Expected translation failure '$ExpectedError', received '$actualError'. Fixture: $translationRoot"
        }
    } elseif ($null -ne $actualError) {
        throw "Unexpected translation failure. Fixture: $translationRoot`n$actualError"
    }
}

Assert-TranslationCheck -Accept @('README.md', 'docs/guide.md')
# Source hashes normalize CRLF, so changing only line endings keeps the review valid.
foreach ($path in @((Join-Path $translationRoot 'README.md'), $englishGuide)) {
    [IO.File]::WriteAllText($path, [IO.File]::ReadAllText($path).Replace("`n", "`r`n"))
}
Assert-TranslationCheck
[IO.File]::AppendAllText($englishGuide, "`r`nUpdated behavior.`r`n")
Assert-TranslationCheck -ExpectedError 'Translation needs review: docs/guide.md -> docs/zh-CN/guide.md'
[IO.File]::AppendAllText($chineseGuide, "`n更新后的行为。`n")
Assert-TranslationCheck -Accept @('docs/guide.md')

$savedTranslation = "$chineseGuide.saved"
Move-Item -LiteralPath $chineseGuide -Destination $savedTranslation
try {
    Assert-TranslationCheck -ExpectedError "Missing translation: docs/zh-CN/guide.md`nBroken link in docs/guide.md: zh-CN/guide.md`nBroken link in README.zh-CN.md: docs/zh-CN/guide.md"
} finally {
    Move-Item -LiteralPath $savedTranslation -Destination $chineseGuide
}
[IO.File]::AppendAllText($englishGuide, "`r`n[Missing page](missing.md)`r`n")
[IO.File]::AppendAllText($chineseGuide, "`n[缺失页面](../missing.md)`n")
# Accepting the source revision must never bypass the independent link check.
Assert-TranslationCheck -Accept @('docs/guide.md') -ExpectedError "Broken link in docs/guide.md: missing.md`nBroken link in docs/zh-CN/guide.md: ../missing.md"
Write-Host 'PASS: translation acceptance, normalized line endings, changed sources, missing translations and broken local links are enforced.'
