param([Parameter(Mandatory = $true)][string]$AssemblyPath,
      [Parameter(Mandatory = $true)][string]$JsonPath,
      [Parameter(Mandatory = $true)][string]$OutputRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$runRoot = Join-Path $OutputRoot ('run-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null
$fixturePath = Join-Path $runRoot 'fixture-config.json'
[ordered]@{
    OAuthClientId = 'fixture-local-oauth'
    CurseForgeAPIKey = 'fixture-local-api'
    CdnSigningSecret = 'fixture-local-signing'
    CdnSigningUserId = '37'
    PCL_CUSTOM_SETTING = 'fixture-local-custom'
} | ConvertTo-Json | Set-Content -LiteralPath $fixturePath -Encoding utf8
# This child process discards its inherited test-relevant variables without reading them.
foreach ($name in @('PCL_MS_CLIENT_ID', 'PCL_CURSEFORGE_API_KEY', 'PCL_CDN_SIGNING_SECRET', 'PCL_CDN_SIGNING_USER_ID', 'PCL_CUSTOM_SETTING', 'PCL_UNDECLARED_TEST_SETTING')) {
    [Environment]::SetEnvironmentVariable($name, [NullString]::Value)
}
[Environment]::SetEnvironmentVariable('PCL_RECOVERED_CONFIG', $fixturePath)
Add-Type -Path $JsonPath
Add-Type -Path $AssemblyPath
[ConfigHeaderChecks]::Run() | Tee-Object -FilePath (Join-Path $OutputRoot 'result.txt')
