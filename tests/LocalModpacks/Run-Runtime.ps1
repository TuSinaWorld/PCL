[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$AssemblyPath,
      [Parameter(Mandatory=$true)][string]$JsonPath,
      [Parameter(Mandatory=$true)][string]$OutputRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -Path $JsonPath
Add-Type -Path $AssemblyPath
$fixtureRoot = Join-Path $OutputRoot ('fixtures-' + [Guid]::NewGuid().ToString('N'))
[LocalModpackChecks]::Run($fixtureRoot) | Tee-Object -FilePath (Join-Path $OutputRoot 'result.txt')
