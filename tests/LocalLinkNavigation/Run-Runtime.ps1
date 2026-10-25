[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$AssemblyPath,
      [Parameter(Mandatory=$true)][string]$JsonPath,
      [Parameter(Mandatory=$true)][string]$OutputRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -Path $JsonPath
Add-Type -Path $AssemblyPath
[LocalLinkNavigationChecks]::Run() | Tee-Object -FilePath (Join-Path $OutputRoot 'result.txt')
