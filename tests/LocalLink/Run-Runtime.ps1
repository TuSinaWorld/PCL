param([string]$AssemblyPath,[string]$JsonPath,[string]$OutputRoot,[string]$Fixture)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
Add-Type -Path $JsonPath
Add-Type -Path $AssemblyPath
$runRoot = Join-Path $OutputRoot ('run-' + [Guid]::NewGuid().ToString('N'))
[LocalLinkChecks]::Run($runRoot, $Fixture) | Tee-Object -FilePath (Join-Path $OutputRoot 'result.txt')
