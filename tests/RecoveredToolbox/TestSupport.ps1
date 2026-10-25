$ErrorActionPreference = 'Stop'
$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$OutputRoot = Join-Path $PSScriptRoot 'bin'
$ProductionSourcePath = Join-Path $RepoRoot 'Plain Craft Launcher 2\Pages\PageOther\PageOtherTest.xaml.vb'
if (-not (Test-Path -LiteralPath $ProductionSourcePath -PathType Leaf)) {
    throw 'Cannot locate the production PageOtherTest.xaml.vb relative to this test directory.'
}
if (-not $env:WINDIR) { throw 'These tests require Windows and the .NET Framework VB compiler.' }
$FrameworkCompiler = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\vbc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\vbc.exe')
) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
if (-not $FrameworkCompiler) { throw 'The .NET Framework v4 VB compiler (vbc.exe) is required.' }
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$ProductionSource = Get-Content -LiteralPath $ProductionSourcePath -Raw -Encoding utf8

function Get-ProductionSection {
    param([Parameter(Mandatory=$true)][string]$Start, [Parameter(Mandatory=$true)][string]$End)
    $first = $ProductionSource.IndexOf($Start, [StringComparison]::Ordinal)
    if ($first -lt 0) { throw ('Production source marker was not found: ' + $Start) }
    $last = $ProductionSource.IndexOf($End, $first, [StringComparison]::Ordinal)
    if ($last -lt 0) { throw ('Production source end marker was not found: ' + $End) }
    return $ProductionSource.Substring($first, $last - $first)
}

function Compile-TestProgram {
    param([Parameter(Mandatory=$true)][string]$SourcePath, [Parameter(Mandatory=$true)][string]$OutputPath)
    & $FrameworkCompiler /nologo /codepage:65001 /target:exe /optioninfer+ /optionstrict+ /reference:System.Core.dll ("/out:" + $OutputPath) $SourcePath
    if ($LASTEXITCODE -ne 0) { throw ('Source-derived test compilation failed: ' + $SourcePath) }
}
