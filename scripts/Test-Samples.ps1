[CmdletBinding()]
param([string]$Package)
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Release.Common.psm1" -Force
$samples = Join-Path (Get-Repo) 'samples'
if ($Package) {
    $work = Reset-Output (Join-Path (Get-Repo) 'artifacts/revision-samples')
    Expand-SafeZip $Package (Join-Path $work 'package')
    $dll = @(Get-ChildItem "$work/package/lib" -Recurse -Filter Fizzy.ImageViewer.dll)
    if ($dll.Count -ne 1) { throw 'Expected one package DLL.' }
    $samples = Join-Path $work 'samples'
    [void][IO.Directory]::CreateDirectory($samples)
    foreach ($source in Get-ChildItem (Join-Path (Get-Repo) 'samples') -Directory) {
        $target = Join-Path $samples $source.Name
        [void][IO.Directory]::CreateDirectory($target)
        Get-ChildItem $source.FullName -File | Copy-Item -Destination $target
    }
    foreach ($project in Get-ChildItem $samples -Recurse -Filter '*.csproj') {
        [xml]$xml = Get-Content $project.FullName -Raw
        foreach ($reference in @($xml.SelectNodes('//ProjectReference'))) { [void]$reference.ParentNode.RemoveChild($reference) }
        $group = $xml.CreateElement('ItemGroup')
        $reference = $xml.CreateElement('Reference'); $reference.SetAttribute('Include', 'Fizzy.ImageViewer')
        $hint = $xml.CreateElement('HintPath'); $hint.InnerText = $dll[0].FullName
        [void]$reference.AppendChild($hint); [void]$group.AppendChild($reference); [void]$xml.Project.AppendChild($group)
        $xml.Save($project.FullName)
    }
}
foreach ($project in Get-ChildItem $samples -Recurse -Filter '*.csproj') {
    Invoke-Checked dotnet @('restore',$project.FullName,'--locked-mode')
    Invoke-Checked dotnet @('build',$project.FullName,'-c','Release','--no-restore')
}
Write-Host 'PASS: samples compiled; WPF examples were not executed.'
