[CmdletBinding()]
param([switch]$IncludeLocalTransport, [switch]$UacProbe)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$toolRoot = Join-Path (Split-Path $repoRoot -Parent) 'analysis\tools'
$dotnet = Join-Path $toolRoot 'dotnet-sdk\dotnet.exe'
$refs = Join-Path $toolRoot 'reference-assemblies-net48.1.0.3\build\.NETFramework'
if (Test-Path -LiteralPath $dotnet) {
    $env:DOTNET_ROOT = Split-Path $dotnet -Parent
    $env:DOTNET_CLI_HOME = Join-Path $toolRoot 'dotnet-home'
} else {
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$output = Join-Path $PSScriptRoot ('bin\run-'+[Guid]::NewGuid().ToString('N'))
$buildArgs = @('msbuild', (Join-Path $PSScriptRoot 'Checks.vbproj'), '-nologo', '-v:minimal', '-maxCpuCount:1', ('-p:OutputPath='+$output+'\'), ('-p:IntermediateOutputPath='+$output+'\obj\'), '-p:UseSharedCompilation=false')
if (Test-Path -LiteralPath $refs) { $buildArgs += @(('-p:FrameworkPathOverride='+$refs+'\v4.8'), ('-p:TargetFrameworkRootPath='+$refs+'\')) }
& $dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { throw 'VLan checks compilation failed.' }
if ($UacProbe) { & (Join-Path $output 'VlanLinkChecks.exe') --uac-probe }
elseif ($IncludeLocalTransport) { & (Join-Path $output 'VlanLinkChecks.exe') }
else { & (Join-Path $output 'VlanLinkChecks.exe') --offline }
if ($LASTEXITCODE -ne 0) { throw 'VLan production-code checks failed.' }
