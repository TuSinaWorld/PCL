[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$sourceRoot = Join-Path $repoRoot 'Plain Craft Launcher 2\Modules'
$outputRoot = Join-Path $PSScriptRoot 'bin'
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $frameworkRoot 'vbc.exe'))) {
    $frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compilerPath = Join-Path $frameworkRoot 'vbc.exe'
$jsonPath = Join-Path $repoRoot 'Plain Craft Launcher 2\Resources\Newtonsoft.Json.dll'
foreach ($required in @($compilerPath, $jsonPath)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Missing prerequisite: $required" }
}
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$references = @('System.dll', 'System.Core.dll', 'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll') | ForEach-Object { Join-Path $frameworkRoot $_ }
$references += $jsonPath
$assemblyPath = Join-Path $outputRoot 'checks.dll'
$compilerArguments = @(
    '/nologo', '/target:library', '/optioninfer+', '/optionstrict+',
    '/imports:System,System.Collections.Generic,System.IO,System.IO.Compression,System.Linq,System.Text,System.Threading,System.Diagnostics,Newtonsoft.Json,Newtonsoft.Json.Linq',
    ('/out:' + $assemblyPath),
    (Join-Path $PSScriptRoot 'Stubs.vb'), (Join-Path $PSScriptRoot 'Checks.vb'),
    (Join-Path $sourceRoot 'ModRecoveredUpdate.vb'), (Join-Path $sourceRoot 'ModRecoveredServer.vb')
)
$compilerArguments += @($references | ForEach-Object { '/reference:' + $_ })
& $compilerPath @compilerArguments *> (Join-Path $outputRoot 'compile.log')
$compileExit = $LASTEXITCODE
$sourceHashes = @{}
foreach ($name in @('ModRecoveredUpdate.vb', 'ModRecoveredServer.vb')) {
    $sourceHashes[$name] = (Get-FileHash -LiteralPath (Join-Path $sourceRoot $name) -Algorithm SHA256).Hash
}
[ordered]@{
    Compiler = 'Framework vbc, VB 2012 language'
    OptionStrict = 'On'
    CompilerExitCode = $compileExit
    ProductionSourcesTransformed = $false
    SourceSHA256 = $sourceHashes
    CompleteProjectBuild = $false
    TargetExecutableExecuted = $false
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputRoot 'compile-result.json') -Encoding utf8
if ($compileExit -ne 0) { throw 'Isolated compile failed; see bin/compile.log.' }
$runtimeShell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
& $runtimeShell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Run-Runtime.ps1') -AssemblyPath $assemblyPath -JsonPath $jsonPath -OutputRoot $outputRoot
if ($LASTEXITCODE -ne 0) { throw 'Update/server checks failed.' }
