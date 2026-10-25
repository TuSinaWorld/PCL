param([Parameter(Mandatory = $true)][string]$AssemblyPath,
      [Parameter(Mandatory = $true)][string]$JsonPath,
      [Parameter(Mandatory = $true)][string]$OutputRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
foreach ($name in @('PCL_MS_CLIENT_ID', 'PCL_CURSEFORGE_API_KEY', 'PCL_CDN_SIGNING_SECRET', 'PCL_CDN_SIGNING_USER_ID', 'PCL_CUSTOM_SETTING', 'OAuthClientId', 'CurseForgeAPIKey')) {
    [Environment]::SetEnvironmentVariable($name, [NullString]::Value)
}
$runRoot = Join-Path $OutputRoot ('run-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null
[Environment]::SetEnvironmentVariable('PCL_RECOVERED_CONFIG', (Join-Path $runRoot 'never-existing-config.json'))
Add-Type -Path $JsonPath
Add-Type -Path $AssemblyPath
[EmbeddedConfigurationChecks]::Run($OutputRoot, $runRoot) | Tee-Object -FilePath (Join-Path $OutputRoot 'result.txt')
