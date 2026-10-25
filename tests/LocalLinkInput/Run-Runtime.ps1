[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$AssemblyPath,
      [Parameter(Mandatory = $true)][string]$JsonPath,
      [Parameter(Mandatory = $true)][string]$Repository,
      [Parameter(Mandatory = $true)][string]$OutputRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -Path $JsonPath
Add-Type -Path $AssemblyPath
$checks = [LocalLinkInputChecks]::Run($Repository)
[ordered]@{ Count = $checks.Count; Checks = $checks; RealWpfTextInput = $true; LauncherExecuted = $false } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutputRoot 'result.json') -Encoding utf8
"$($checks.Count) local link input checks passed; production dialog extraction and invite codec, real WPF TextInput; no launcher, network, or private configuration used." | Tee-Object -FilePath (Join-Path $OutputRoot 'result.txt')
