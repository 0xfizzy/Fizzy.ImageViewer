[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Prepare','Check','Start','Status','Resume','Rollback')][string]$Action,
    [Parameter(Mandatory)][ValidatePattern('^v[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$')][string]$Tag,
    [ValidateRange(0,2147483647)][int]$Revision = 0,
    [string]$Branch
)
$ErrorActionPreference = 'Stop'
$parameters = @{ Action = $Action; Version = $Tag.Substring(1); Revision = $Revision }
if ($Branch) { $parameters.SourceRef = $Branch }
& "$PSScriptRoot/Documentation-Revision.ps1" @parameters
