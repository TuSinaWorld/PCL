[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$sourcePath = Join-Path $repoRoot 'Plain Craft Launcher 2\Modules\Minecraft\ModModpackPolicy.vb'
$outputRoot = Join-Path $PSScriptRoot 'bin'
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $frameworkRoot 'vbc.exe'))) {
    $frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compilerPath = Join-Path $frameworkRoot 'vbc.exe'
$jsonPath = Join-Path $repoRoot 'Plain Craft Launcher 2\Resources\Newtonsoft.Json.dll'
foreach ($required in @($sourcePath, $compilerPath, $jsonPath)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Missing prerequisite: $required" }
}
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$references = @('System.dll', 'System.Core.dll', 'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll') | ForEach-Object { Join-Path $frameworkRoot $_ }
$references += $jsonPath
$assemblyPath = Join-Path $outputRoot 'checks.dll'
$compilerArguments = @('/nologo', '/target:library', '/optioninfer+', '/optionstrict+',
    ('/out:' + $assemblyPath), $sourcePath, (Join-Path $PSScriptRoot 'Checks.vb'))
$compilerArguments += @($references | ForEach-Object { '/reference:' + $_ })
& $compilerPath @compilerArguments *> (Join-Path $outputRoot 'compile.log')
$compileExit = $LASTEXITCODE
[ordered]@{
    CompilerExitCode = $compileExit
    ProductionSourcesTransformed = $false
    SourceSHA256 = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
    TargetExecutableExecuted = $false
    CompleteProjectBuild = $false
} | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $outputRoot 'compile-result.json') -Encoding utf8
if ($compileExit -ne 0) { throw 'Isolated modpack compile failed; see bin/compile.log.' }
$runtimeShell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
& $runtimeShell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Run-Runtime.ps1') -AssemblyPath $assemblyPath -JsonPath $jsonPath -OutputRoot $outputRoot
if ($LASTEXITCODE -ne 0) { throw 'Local modpack checks failed.' }
