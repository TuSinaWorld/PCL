[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$outputRoot = Join-Path $PSScriptRoot 'bin'
$generatedRoot = Join-Path $outputRoot 'generated'
New-Item -ItemType Directory -Force -Path $generatedRoot | Out-Null
$themePath = Join-Path $repoRoot 'Plain Craft Launcher 2\Modules\ModRecoveredTheme.vb'
$basePath = Join-Path $repoRoot 'Plain Craft Launcher 2\Modules\Base\ModBase.vb'
$uiPath = Join-Path $repoRoot 'Plain Craft Launcher 2\Pages\PageSetup\PageSetupUI.xaml.vb'
$colorSource = [regex]::Match([IO.File]::ReadAllText($basePath), '(?ms)^    Public Class MyColor\r?\n.*?^    End Class\b').Value
if (-not $colorSource) { throw 'Could not extract the production MyColor class.' }
# Framework VB 2012 cannot continue an array initializer after a trailing comment.
# Remove those four comments in the generated test copy only; keep every numeric value unchanged.
$colorSource = [regex]::Replace($colorSource, "(,[ `t]*)'[^`r`n]*", '$1')
# MyColor itself uses the upstream Option Strict Off conversions and late binding.
[IO.File]::WriteAllText((Join-Path $generatedRoot 'MyColor.vb'), "Option Strict Off`r`nImports Media = System.Windows.Media`r`n" + $colorSource, [Text.UTF8Encoding]::new($false))
$sliderHandler = [regex]::Match([IO.File]::ReadAllText($uiPath), '(?ms)^    Private Sub HSL_Change\(\).*?^    End Sub\b').Value
if (-not $sliderHandler) { throw 'Could not extract the production slider handler.' }
$fixture = @'
Public Class SliderFixture
    Public WithEvents SliderLauncherHue As New TestSlider
    Public WithEvents SliderLauncherSat As New TestSlider
    Public WithEvents SliderLauncherDelta As New TestSlider
    Public WithEvents SliderLauncherLight As New TestSlider
    Public RadioLauncherTheme14 As New TestRadio
'@
[IO.File]::WriteAllText((Join-Path $generatedRoot 'SliderFixture.vb'), $fixture + "`r`n" + $sliderHandler + "`r`nEnd Class`r`n", [Text.UTF8Encoding]::new($false))
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $frameworkRoot 'vbc.exe'))) { $frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }
$compilerPath = Join-Path $frameworkRoot 'vbc.exe'
$references = @('System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Xml.dll', 'System.Xml.Linq.dll', 'System.Xaml.dll') | ForEach-Object { Join-Path $frameworkRoot $_ }
$references += @('WindowsBase.dll', 'PresentationCore.dll', 'PresentationFramework.dll') | ForEach-Object { Join-Path (Join-Path $frameworkRoot 'WPF') $_ }
foreach ($required in @($compilerPath) + $references) { if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Missing prerequisite: $required" } }
$assemblyPath = Join-Path $outputRoot 'checks.dll'
$compilerArguments = @('/nologo', '/target:library', '/optioninfer+', '/optionstrict+',
    '/imports:System,System.Collections.Generic,System.Linq,System.Threading,System.Windows,System.Windows.Controls,System.Windows.Media',
    ('/out:' + $assemblyPath), (Join-Path $PSScriptRoot 'Stubs.vb'), (Join-Path $PSScriptRoot 'Checks.vb'),
    (Join-Path $generatedRoot 'MyColor.vb'), (Join-Path $generatedRoot 'SliderFixture.vb'), $themePath)
$compilerArguments += @($references | ForEach-Object { '/reference:' + $_ })
& $compilerPath @compilerArguments *> (Join-Path $outputRoot 'compile.log')
$compileExit = $LASTEXITCODE
[ordered]@{
    CompilerExitCode = $compileExit
    ThemeModuleCompiledUnchanged = $true
    ProductionExtractions = @('complete MyColor class, original Option Strict Off semantics; four array comments removed for VB 2012', 'complete HSL_Change handler, unchanged')
    ThemeSHA256 = (Get-FileHash -LiteralPath $themePath -Algorithm SHA256).Hash
    UiSourceSHA256 = (Get-FileHash -LiteralPath $uiPath -Algorithm SHA256).Hash
    RealWpfBrushesAndColorConversions = $true
    LauncherExecuted = $false
    IdentityOrCredentialConfigurationUsed = $false
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $outputRoot 'compile-result.json') -Encoding utf8
if ($compileExit -ne 0) { throw 'Local theme compile failed; see bin/compile.log.' }
$runtimeShell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
& $runtimeShell -NoProfile -STA -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Run-Runtime.ps1') -AssemblyPath $assemblyPath -Repository $repoRoot -OutputRoot $outputRoot
if ($LASTEXITCODE -ne 0) { throw 'Local theme behavior checks failed.' }
