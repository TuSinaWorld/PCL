param([Parameter(Mandatory = $true)][string]$AssemblyPath,
      [Parameter(Mandatory = $true)][string]$Repository,
      [Parameter(Mandatory = $true)][string]$OutputRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -Path $AssemblyPath
$checks = [LocalThemeChecks]::Run($Repository)
[ordered]@{ Count = $checks.Count; Checks = $checks; LauncherExecuted = $false } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutputRoot 'result.json') -Encoding utf8
$message = "$($checks.Count) local theme checks passed; no launcher, service, credentials, or authorization context was used."
$message | Tee-Object -FilePath (Join-Path $OutputRoot 'result.txt')
