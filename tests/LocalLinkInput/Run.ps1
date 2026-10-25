[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$outputRoot = Join-Path $PSScriptRoot 'bin'
$generatedRoot = Join-Path $outputRoot 'generated'
New-Item -ItemType Directory -Force -Path $generatedRoot | Out-Null
$mainPath = Join-Path $repoRoot 'Plain Craft Launcher 2\Modules\ModMain.vb'
$inputPath = Join-Path $repoRoot 'Plain Craft Launcher 2\Controls\MyMsg\MyMsgInput.xaml.vb'
$codecPath = Join-Path $repoRoot 'Plain Craft Launcher 2\Modules\ModLocalLink.vb'
$mainSource = [IO.File]::ReadAllText($mainPath)
$inputSource = [IO.File]::ReadAllText($inputPath)
function Get-ProductionBlock([string]$Source, [string]$Pattern, [string]$Name) {
    $matches = [regex]::Matches($Source, $Pattern)
    if ($matches.Count -ne 1) { throw "Expected exactly one production $Name; found $($matches.Count)." }
    return $matches[0].Value
}
$converter = Get-ProductionBlock $mainSource '(?ms)^    Public Class MyMsgBoxConverter\r?\n.*?^    End Class\b' 'converter class'
$enum = Get-ProductionBlock $mainSource '(?ms)^    Public Enum MyMsgBoxType\r?\n.*?^    End Enum\b' 'dialog type enum'
$inputMethod = Get-ProductionBlock $mainSource '(?ms)^    Public Function MyMsgBoxInput\(.*?^    End Function\b' 'input dialog function'
$constructor = Get-ProductionBlock $inputSource '(?ms)^    Public Sub New\(Converter As MyMsgBoxConverter\)\r?\n.*?^    End Sub\b' 'input control constructor'
$confirm = Get-ProductionBlock $inputSource '(?ms)^    Public Sub Btn1_Click\(\) Handles Btn1\.Click\r?\n.*?^    End Sub\b' 'input confirm handler'
$cancel = Get-ProductionBlock $inputSource '(?ms)^    Public Sub Btn2_Click\(\) Handles Btn2\.Click\r?\n.*?^    End Sub\b' 'input cancel handler'
$mainFixture = "Option Strict Off`r`nImports System.Collections.ObjectModel`r`nImports System.Windows.Interop`r`nImports System.Windows.Threading`r`nPublic Module ModMain`r`n" + $converter + "`r`n" + $enum + "`r`n" + $inputMethod + "`r`nEnd Module`r`n"
# Keep the original extraction for inspection. Only syntax unsupported by Framework VB 2012 is lowered in the compiled copy.
[IO.File]::WriteAllText((Join-Path $generatedRoot 'ModMain.original.vb.txt'), $mainFixture, [Text.UTF8Encoding]::new($false))
$mainFixture = $mainFixture.Replace('NameOf(MaxLength)', '"MaxLength"')
$mainFixture = $mainFixture.Replace('$"输入弹框返回：{Converter.InputResultLogText}"', 'String.Format("输入弹框返回：{0}", Converter.InputResultLogText)')
if ($mainFixture.Contains('NameOf(') -or $mainFixture.Contains('$"')) { throw 'Unhandled modern VB syntax in extracted input function.' }
[IO.File]::WriteAllText((Join-Path $generatedRoot 'ModMain.vb'), $mainFixture, [Text.UTF8Encoding]::new($false))
$controlFixture = "Option Strict Off`r`nPartial Public Class MyMsgInput`r`n" + $constructor + "`r`n" + $confirm + "`r`n" + $cancel + "`r`nEnd Class`r`n"
[IO.File]::WriteAllText((Join-Path $generatedRoot 'MyMsgInput.vb'), $controlFixture, [Text.UTF8Encoding]::new($false))
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $frameworkRoot 'vbc.exe'))) { $frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }
$compilerPath = Join-Path $frameworkRoot 'vbc.exe'
$jsonPath = Join-Path $repoRoot 'Plain Craft Launcher 2\Resources\Newtonsoft.Json.dll'
$references = @('System.dll', 'System.Core.dll', 'System.Xml.dll', 'System.Xml.Linq.dll', 'System.Xaml.dll', 'System.Net.Http.dll', 'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll') | ForEach-Object { Join-Path $frameworkRoot $_ }
$references += @('WindowsBase.dll', 'PresentationCore.dll', 'PresentationFramework.dll') | ForEach-Object { Join-Path (Join-Path $frameworkRoot 'WPF') $_ }
$references += $jsonPath
foreach ($required in @($compilerPath) + $references) { if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Missing prerequisite: $required" } }
$assemblyPath = Join-Path $outputRoot 'checks.dll'
$compilerArguments = @('/nologo', '/target:library', '/optioninfer+', '/optionstrict+',
    '/imports:System,System.Collections.Generic,System.Collections.ObjectModel,System.Linq,System.Threading,System.Windows,System.Windows.Controls,System.Windows.Input,System.Windows.Shapes,System.Windows.Threading',
    ('/out:' + $assemblyPath), (Join-Path $PSScriptRoot 'Stubs.vb'), (Join-Path $PSScriptRoot 'Checks.vb'),
    (Join-Path $generatedRoot 'ModMain.vb'), (Join-Path $generatedRoot 'MyMsgInput.vb'), $codecPath)
$compilerArguments += @($references | ForEach-Object { '/reference:' + $_ })
& $compilerPath @compilerArguments *> (Join-Path $outputRoot 'compile.log')
$compileExit = $LASTEXITCODE
[ordered]@{
    CompilerExitCode = $compileExit
    ProductionExtractions = @('complete MyMsgBoxConverter class and MyMsgBoxType enum', 'complete MyMsgBoxInput function', 'complete MyMsgInput constructor and confirm/cancel handlers')
    TestCopyTransformations = @('NameOf(MaxLength) lowered to identical string constant', 'one input-return interpolation lowered to String.Format', 'original implicit Object conversions retained with Option Strict Off')
    SourceSHA256 = [ordered]@{
        ModMain = (Get-FileHash -LiteralPath $mainPath -Algorithm SHA256).Hash
        MyMsgInput = (Get-FileHash -LiteralPath $inputPath -Algorithm SHA256).Hash
        ModLocalLink = (Get-FileHash -LiteralPath $codecPath -Algorithm SHA256).Hash
    }
    ProductionInviteCodecCompiledUnchanged = $true
    RealWpfTextInputAndMaxLength = $true
    LauncherExecuted = $false
    NetworkOrPrivateConfigurationUsed = $false
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputRoot 'compile-result.json') -Encoding utf8
if ($compileExit -ne 0) { Get-Content -LiteralPath (Join-Path $outputRoot 'compile.log'); throw 'Local link input compile failed.' }
$runtimeShell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
& $runtimeShell -NoProfile -NonInteractive -STA -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Run-Runtime.ps1') -AssemblyPath $assemblyPath -JsonPath $jsonPath -Repository $repoRoot -OutputRoot $outputRoot
if ($LASTEXITCODE -ne 0) { throw 'Local link input behavior checks failed.' }
