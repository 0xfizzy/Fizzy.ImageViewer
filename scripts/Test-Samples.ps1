[CmdletBinding()]
param([string]$Package)
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Release.Common.psm1" -Force
$samples = Join-Path (Get-Repo) 'samples'
if ($Package) {
    $work = Reset-Output (Join-Path (Get-Repo) 'artifacts/package-samples')
    Expand-SafeZip $Package (Join-Path $work 'package')
    $spec = @(Get-ChildItem "$work/package" -Filter '*.nuspec')
    if ($spec.Count -ne 1) { throw 'Expected one package nuspec.' }
    [xml]$metadata = Get-Content $spec[0].FullName -Raw
    $packageId = $metadata.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]/*[local-name()="id"]').InnerText
    $packageVersion = $metadata.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]/*[local-name()="version"]').InnerText
    if ($packageId -cne 'Fizzy.ImageViewer') { throw 'Expected original Fizzy.ImageViewer package.' }
    [void](Get-Version $packageVersion)
    $feed = Join-Path $work 'feed'
    [void][IO.Directory]::CreateDirectory($feed)
    Copy-Item -LiteralPath $Package -Destination (Join-Path $feed "$packageId.$packageVersion.nupkg")
    # Map the tested package exclusively to the retained original; restore its declared
    # transitive dependencies from NuGet into a fresh cache, never from a local source build.
    $configPath = Join-Path $work 'NuGet.Config'
    $escapedFeed = [Security.SecurityElement]::Escape($feed)
    @"
<configuration>
  <packageSources><clear/><add key="original" value="$escapedFeed"/><add key="nuget" value="https://api.nuget.org/v3/index.json"/></packageSources>
  <packageSourceMapping><clear/><packageSource key="original"><package pattern="Fizzy.ImageViewer"/></packageSource><packageSource key="nuget"><package pattern="*"/></packageSource></packageSourceMapping>
</configuration>
"@ | Set-Content -LiteralPath $configPath -Encoding utf8NoBOM
    $samples = Join-Path $work 'samples'
    [void][IO.Directory]::CreateDirectory($samples)
    foreach ($source in Get-ChildItem (Join-Path (Get-Repo) 'samples') -Directory) {
        $target = Join-Path $samples $source.Name
        [void][IO.Directory]::CreateDirectory($target)
        Get-ChildItem $source.FullName -File | Where-Object Name -NE 'packages.lock.json' | Copy-Item -Destination $target
    }
    foreach ($project in Get-ChildItem $samples -Recurse -Filter '*.csproj') {
        [xml]$xml = Get-Content $project.FullName -Raw
        foreach ($reference in @($xml.SelectNodes('//ProjectReference'))) { [void]$reference.ParentNode.RemoveChild($reference) }
        $group = $xml.CreateElement('ItemGroup')
        $reference = $xml.CreateElement('PackageReference'); $reference.SetAttribute('Include', $packageId)
        $reference.SetAttribute('Version', "[$packageVersion]")
        [void]$group.AppendChild($reference); [void]$xml.Project.AppendChild($group)
        $xml.Save($project.FullName)
    }
}
foreach ($project in Get-ChildItem $samples -Recurse -Filter '*.csproj') {
    if ($Package) {
        Invoke-Checked dotnet @('restore',$project.FullName,'--force-evaluate','--configfile',$configPath,'--packages',(Join-Path $work 'packages'))
        $restoredPackage = Join-Path $work "packages/$($packageId.ToLowerInvariant())/$packageVersion/$($packageId.ToLowerInvariant()).$packageVersion.nupkg"
        Assert-PackageEqual $Package $restoredPackage
    } else { Invoke-Checked dotnet @('restore',$project.FullName,'--locked-mode') }
    Invoke-Checked dotnet @('build',$project.FullName,'-c','Release','--no-restore')
}
Write-Host 'PASS: samples compiled; WPF examples were not executed.'
