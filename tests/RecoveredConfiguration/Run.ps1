[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$sourceRoot = Join-Path $repoRoot 'Plain Craft Launcher 2\Modules'
$outputRoot = Join-Path $PSScriptRoot 'bin'
$generatedRoot = Join-Path $outputRoot 'generated'
New-Item -ItemType Directory -Force -Path $generatedRoot | Out-Null
$sourceHashes = @{}
foreach ($name in @('ModSecret.vb', 'ModRecoveredConfig.vb')) {
    $sourcePath = Join-Path $sourceRoot $name
    $sourceHashes[$name] = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
    $source = [IO.File]::ReadAllText($sourcePath)
    # VB 2012 compatibility lowering happens in bin/generated only, never in production files.
    $source = $source.Replace('NameOf(Name)', '"Name"').Replace('NameOf(UrlWithMark)', '"UrlWithMark"').Replace('NameOf(Req)', '"Req"')
    $source = $source.Replace('$"PCL2/{VersionBaseName}.{CInt(BuildType)} Mozilla/5.0 AppleWebKit/537.36 Chrome/63.0.3239.132 Safari/537.36"', 'String.Format("PCL2/{0}.{1} Mozilla/5.0 AppleWebKit/537.36 Chrome/63.0.3239.132 Safari/537.36", VersionBaseName, CInt(BuildType))')
    $source = $source.Replace('$"PCL2/{VersionBaseName}.{CInt(BuildType)}"', 'String.Format("PCL2/{0}.{1}", VersionBaseName, CInt(BuildType))')
    $source = $source.Replace('$"http://{VersionCode}.open.pcl2.server/"', 'String.Format("http://{0}.open.pcl2.server/", VersionCode)')
    if ($source.Contains('NameOf(') -or $source.Contains('$"')) { throw 'Unhandled modern VB syntax in generated test copy.' }
    [IO.File]::WriteAllText((Join-Path $generatedRoot $name), $source, [Text.UTF8Encoding]::new($false))
}
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $frameworkRoot 'vbc.exe'))) {
    $frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compilerPath = Join-Path $frameworkRoot 'vbc.exe'
$jsonPath = Join-Path $repoRoot 'Plain Craft Launcher 2\Resources\Newtonsoft.Json.dll'
foreach ($required in @($compilerPath, $jsonPath)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Missing prerequisite: $required" }
}
$references = @('System.dll', 'System.Core.dll', 'System.Net.Http.dll') | ForEach-Object { Join-Path $frameworkRoot $_ }
$references += $jsonPath
$assemblyPath = Join-Path $outputRoot 'checks.dll'
$compilerArguments = @('/nologo', '/target:library', '/optioninfer+', '/optionstrict+',
    '/imports:System,System.Collections.Generic,System.IO,System.Linq,System.Text,System.Net.Http,Newtonsoft.Json.Linq',
    ('/out:' + $assemblyPath),
    (Join-Path $PSScriptRoot 'Checks.vb'), (Join-Path $generatedRoot 'ModSecret.vb'), (Join-Path $generatedRoot 'ModRecoveredConfig.vb'))
$compilerArguments += @($references | ForEach-Object { '/reference:' + $_ })
& $compilerPath @compilerArguments *> (Join-Path $outputRoot 'compile.log')
$compileExit = $LASTEXITCODE
[ordered]@{
    Compiler = 'Framework vbc, VB 2012 language'
    OptionStrict = 'On'
    CompilerExitCode = $compileExit
    ProductionFilesEdited = $false
    TestCopyTransformations = @('NameOf lowered to same string constants', 'Three UA/Referer interpolations lowered to String.Format')
    SourceSHA256 = $sourceHashes
    HashRandomTimeDependenciesStubbed = $true
    CompleteProjectBuild = $false
    ProductionConfigurationUsedByTestedCode = $false
    RealCredentialValuesLogged = $false
    TargetExecutableExecuted = $false
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputRoot 'compile-result.json') -Encoding utf8
if ($compileExit -ne 0) { throw 'Isolated configuration/header compile failed; see bin/compile.log.' }
# A separate process receives only fixture settings. The parent never reads inherited credential values.
$runtimeShell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
& $runtimeShell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Run-Runtime.ps1') -AssemblyPath $assemblyPath -JsonPath $jsonPath -OutputRoot $outputRoot
if ($LASTEXITCODE -ne 0) { throw 'Configuration/header checks failed.' }
