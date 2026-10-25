[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$outputRoot = Join-Path $PSScriptRoot 'bin'
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $frameworkRoot 'vbc.exe'))) { $frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$jsonPath = Join-Path $repoRoot 'Plain Craft Launcher 2\Resources\Newtonsoft.Json.dll'
$assemblyPath = Join-Path $outputRoot 'checks.dll'
$fixturePath = Join-Path $outputRoot 'LocalLinkFixture.exe'
& (Join-Path $frameworkRoot 'csc.exe') /nologo /target:exe ('/out:' + $fixturePath) (Join-Path $PSScriptRoot 'ProcessFixture.cs') *> (Join-Path $outputRoot 'fixture-compile.log')
if ($LASTEXITCODE -ne 0) { throw 'Process fixture compile failed.' }
$references = @('System.dll','System.Core.dll','System.Net.Http.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll') | ForEach-Object { '/reference:' + (Join-Path $frameworkRoot $_) }
& (Join-Path $frameworkRoot 'vbc.exe') /nologo /target:library /optioninfer+ /optionstrict+ ('/out:' + $assemblyPath) ('/reference:' + $jsonPath) @references (Join-Path $PSScriptRoot 'Checks.vb') (Join-Path $repoRoot 'Plain Craft Launcher 2\Modules\ModLocalLink.vb') *> (Join-Path $outputRoot 'compile.log')
if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath (Join-Path $outputRoot 'compile.log'); throw 'LocalLink checks compile failed.' }
$runtimeShell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
& $runtimeShell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Run-Runtime.ps1') -AssemblyPath $assemblyPath -JsonPath $jsonPath -OutputRoot $outputRoot -Fixture $fixturePath
if ($LASTEXITCODE -ne 0) { throw 'LocalLink checks failed.' }
