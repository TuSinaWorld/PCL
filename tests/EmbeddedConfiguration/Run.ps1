[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$outputRoot = Join-Path $PSScriptRoot 'bin'
$generatedRoot = Join-Path $outputRoot 'generated'
New-Item -ItemType Directory -Force -Path $generatedRoot | Out-Null
$sourcePath = Join-Path $repoRoot 'Plain Craft Launcher 2\Modules\ModRecoveredConfig.vb'
$sourceHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
# Only the generated test copy is lowered for Framework vbc's VB 2012 syntax.
$source = [IO.File]::ReadAllText($sourcePath).Replace('NameOf(Name)', '"Name"')
if ($source.Contains('NameOf(') -or $source.Contains('$"')) { throw 'Unhandled modern VB syntax in generated test copy.' }
$generatedSource = Join-Path $generatedRoot 'ModRecoveredConfig.vb'
[IO.File]::WriteAllText($generatedSource, $source, [Text.UTF8Encoding]::new($false))
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $frameworkRoot 'vbc.exe'))) {
    $frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compilerPath = Join-Path $frameworkRoot 'vbc.exe'
$jsonPath = Join-Path $repoRoot 'Plain Craft Launcher 2\Resources\Newtonsoft.Json.dll'
foreach ($required in @($compilerPath, $jsonPath)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Missing prerequisite: $required" }
}
$references = @('System.dll', 'System.Core.dll') | ForEach-Object { Join-Path $frameworkRoot $_ }
$references += $jsonPath
$baseArguments = @('/nologo', '/target:library', '/optioninfer+', '/optionstrict+', '/define:TRACE=True',
    '/imports:System,System.Collections.Generic,System.IO,System.Text,Newtonsoft.Json.Linq')
$baseArguments += @($references | ForEach-Object { '/reference:' + $_ })
$fixtureNames = [Collections.Generic.List[string]]::new()
function Build-Fixture {
    param([string]$Name, [AllowNull()]$Json, [string]$ResourceName = 'PCL.EmbeddedClientConfig.json', [byte[]]$Bytes = $null)
    $assemblyPath = Join-Path $outputRoot ($Name + '.dll')
    $arguments = $baseArguments + @(('/out:' + $assemblyPath), $generatedSource)
    if ($null -ne $Json -or $null -ne $Bytes) {
        $resourcePath = Join-Path $generatedRoot ($Name + '.json')
        if ($null -ne $Bytes) { [IO.File]::WriteAllBytes($resourcePath, $Bytes) }
        else { [IO.File]::WriteAllText($resourcePath, $Json, [Text.UTF8Encoding]::new($false)) }
        $arguments += '/resource:' + $resourcePath + ',' + $ResourceName
    }
    & $compilerPath @arguments *> (Join-Path $outputRoot ($Name + '.compile.log'))
    if ($LASTEXITCODE -ne 0) { throw "Production module fixture compile failed: $Name" }
    $fixtureNames.Add($Name)
}
$valid = '{"OAuthClientId":"SYNTHETIC_ONLY_embedded_oauth","CurseForgeAPIKey":"SYNTHETIC_ONLY_embedded_api"}'
foreach ($name in @('embedded-only', 'local-overrides', 'local-empty', 'local-missing', 'local-invalid', 'local-oversize')) {
    Build-Fixture -Name $name -Json $valid
}
Build-Fixture -Name 'embedded-empty' -Json '{"OAuthClientId":"","CurseForgeAPIKey":null}'
Build-Fixture -Name 'embedded-missing' -Json '{"OAuthClientId":"SYNTHETIC_ONLY_partial_oauth"}'
Build-Fixture -Name 'embedded-second' -Json '{"OAuthClientId":"SYNTHETIC_ONLY_second_oauth","CurseForgeAPIKey":"SYNTHETIC_ONLY_second_api"}'
Build-Fixture -Name 'embedded-date-string' -Json '{"OAuthClientId":"2030-01-02T03:04:05Z","CurseForgeAPIKey":"SYNTHETIC_ONLY_date_fixture_api"}'
Build-Fixture -Name 'no-resource' -Json $null
Build-Fixture -Name 'wrong-resource-name' -Json $valid -ResourceName 'Other.EmbeddedClientConfig.json'
Build-Fixture -Name 'embedded-corrupt' -Json '{"OAuthClientId":"SYNTHETIC_ONLY_corrupt",invalid'
Build-Fixture -Name 'embedded-unknown' -Json '{"OAuthClientId":"SYNTHETIC_ONLY_unknown","CdnSigningSecret":"SYNTHETIC_ONLY_never_allowed","CdnSigningUserId":"99"}'
Build-Fixture -Name 'embedded-extra' -Json '{"OAuthClientId":"SYNTHETIC_ONLY_extra","PCL_CUSTOM_SETTING":"SYNTHETIC_ONLY_never_allowed"}'
Build-Fixture -Name 'embedded-env-name' -Json '{"PCL_MS_CLIENT_ID":"SYNTHETIC_ONLY_alias_not_allowed"}'
Build-Fixture -Name 'embedded-number' -Json '{"OAuthClientId":"SYNTHETIC_ONLY_number","CurseForgeAPIKey":123}'
Build-Fixture -Name 'embedded-boolean' -Json '{"OAuthClientId":true}'
Build-Fixture -Name 'embedded-object' -Json '{"OAuthClientId":{}}'
Build-Fixture -Name 'embedded-array-value' -Json '{"CurseForgeAPIKey":[]}'
Build-Fixture -Name 'embedded-duplicate' -Json '{"OAuthClientId":"SYNTHETIC_ONLY_first","OAuthClientId":"SYNTHETIC_ONLY_second"}'
Build-Fixture -Name 'embedded-case-duplicate' -Json '{"OAuthClientId":"SYNTHETIC_ONLY_first","oauthclientid":"SYNTHETIC_ONLY_second"}'
Build-Fixture -Name 'embedded-null-root' -Json 'null'
Build-Fixture -Name 'embedded-array-root' -Json '[]'
Build-Fixture -Name 'embedded-trailing' -Json ($valid + '{}')
Build-Fixture -Name 'embedded-invalid-utf8' -Json $null -Bytes ([byte[]]@(123, 34, 255, 34, 58, 34, 120, 34, 125))
Build-Fixture -Name 'embedded-oversize' -Json ('{"OAuthClientId":"' + ('x' * (1024 * 1024)) + '"}')
$hostResource = Join-Path $generatedRoot 'host-decoy.json'
[IO.File]::WriteAllText($hostResource, '{"OAuthClientId":"SYNTHETIC_ONLY_host_decoy","CurseForgeAPIKey":"SYNTHETIC_ONLY_host_decoy"}', [Text.UTF8Encoding]::new($false))
$harnessPath = Join-Path $outputRoot 'checks.dll'
$harnessArguments = $baseArguments + @(('/out:' + $harnessPath), (Join-Path $PSScriptRoot 'Checks.vb'), ('/resource:' + $hostResource + ',PCL.EmbeddedClientConfig.json'))
& $compilerPath @harnessArguments *> (Join-Path $outputRoot 'harness.compile.log')
if ($LASTEXITCODE -ne 0) { throw 'Embedded configuration harness compile failed; see bin/harness.compile.log.' }
[ordered]@{
    Compiler = 'Framework vbc, VB 2012 language'
    OptionStrict = 'On'
    TestCopyTransformations = @('NameOf lowered to same string constant')
    ProductionSourceSHA256 = $sourceHash
    ProductionFilesEditedByHarness = $false
    CompiledProductionModuleFixtureCount = $fixtureNames.Count
    FixtureAssemblyNames = $fixtureNames.ToArray()
    CompleteProjectBuild = $false
    RealCredentialValuesReadOrLogged = $false
    TargetExecutableExecuted = $false
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputRoot 'compile-result.json') -Encoding utf8
# The child clears relevant inherited variables without reading them and selects only generated local paths.
$runtimeShell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
& $runtimeShell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Run-Runtime.ps1') -AssemblyPath $harnessPath -JsonPath $jsonPath -OutputRoot $outputRoot
if ($LASTEXITCODE -ne 0) { throw 'Embedded configuration runtime checks failed.' }
