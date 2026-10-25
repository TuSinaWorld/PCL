[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$outputRoot = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$sourcePath = Join-Path $repoRoot 'Plain Craft Launcher 2\FormMain.xaml.vb'
$source = [IO.File]::ReadAllText($sourcePath)
$match = [regex]::Match($source, '(?ms)^    Private Sub FormMain_Activated\(\) Handles Me\.Activated\r?\n.*?^    End Sub\r?$')
if (-not $match.Success) { throw 'Cannot locate exact production activation method.' }
$extractedPath = Join-Path $outputRoot 'Activated.vb'
[IO.File]::WriteAllText($extractedPath, "Imports System`r`nImports System.Text`r`nPartial Public Class FormMain`r`n" + $match.Value + "`r`nEnd Class`r`n", [Text.UTF8Encoding]::new($false))
$eventSource = [IO.File]::ReadAllText((Join-Path $repoRoot 'Plain Craft Launcher 2\Modules\ModEvent.vb'))
$eventMethod = [regex]::Match($eventSource, '(?ms)^    Public Shared Sub Raise\(Type As EventType, Arg As String\)\r?\n.*?^    End Sub\r?$')
if (-not $eventMethod.Success) { throw 'Cannot locate production custom-event method.' }
$eventHeader = $eventMethod.Value.Substring(0, $eventMethod.Value.IndexOf('            Select Case Type'))
$joinName = -join ([char[]]@(0x52A0, 0x5165, 0x623F, 0x95F4))
$updateName = -join ([char[]]@(0x68C0, 0x67E5, 0x66F4, 0x65B0))
$joinPattern = '(?ms)^                Case EventType\.' + [regex]::Escape($joinName) + '\r?\n.*?(?=^                Case EventType\.' + [regex]::Escape($updateName) + ')'
$joinBranch = [regex]::Match($eventMethod.Value, $joinPattern)
$eventCatch = [regex]::Match($eventMethod.Value, '(?ms)^        Catch ex As Exception\r?\n.*?^    End Sub\r?$')
if (-not $joinBranch.Success -or -not $eventCatch.Success) { throw 'Cannot locate exact room-event branch and error handling.' }
$eventPath = Join-Path $outputRoot 'RoomEvent.vb'
$eventFixture = "Imports System`r`nPartial Public Class CustomEvent`r`n    Public Enum EventType`r`n        None = 0`r`n        " + $joinName + " = 1`r`n    End Enum`r`n" + $eventHeader + "            Select Case Type`r`n" + $joinBranch.Value + "            End Select`r`n" + $eventCatch.Value + "`r`nEnd Class`r`n"
[IO.File]::WriteAllText($eventPath, $eventFixture, [Text.UTF8Encoding]::new($false))
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $frameworkRoot 'vbc.exe'))) { $frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }
$jsonPath = Join-Path $repoRoot 'Plain Craft Launcher 2\Resources\Newtonsoft.Json.dll'
$assemblyPath = Join-Path $outputRoot 'checks.dll'
$references = @('System.dll', 'System.Core.dll', 'System.Net.Http.dll', 'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll') | ForEach-Object { '/reference:' + (Join-Path $frameworkRoot $_) }
& (Join-Path $frameworkRoot 'vbc.exe') /nologo /target:library /optioninfer+ /optionstrict+ ('/out:' + $assemblyPath) ('/reference:' + $jsonPath) @references $extractedPath $eventPath (Join-Path $PSScriptRoot 'Stubs.vb') (Join-Path $PSScriptRoot 'Checks.vb') (Join-Path $repoRoot 'Plain Craft Launcher 2\Modules\ModLocalLink.vb') *> (Join-Path $outputRoot 'compile.log')
if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath (Join-Path $outputRoot 'compile.log'); throw 'Navigation checks compile failed.' }
$runtimeShell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
& $runtimeShell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Run-Runtime.ps1') -AssemblyPath $assemblyPath -JsonPath $jsonPath -OutputRoot $outputRoot
if ($LASTEXITCODE -ne 0) { throw 'Navigation checks failed.' }
