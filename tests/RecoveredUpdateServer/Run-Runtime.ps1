param([Parameter(Mandatory = $true)][string]$AssemblyPath,
      [Parameter(Mandatory = $true)][string]$JsonPath,
      [Parameter(Mandatory = $true)][string]$OutputRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -Path $JsonPath
Add-Type -Path $AssemblyPath
$runRoot = Join-Path $OutputRoot ('run-' + [Guid]::NewGuid().ToString('N'))
[RecoveredUpdateServerChecks]::Run($runRoot) | Tee-Object -FilePath (Join-Path $OutputRoot 'result.txt')
