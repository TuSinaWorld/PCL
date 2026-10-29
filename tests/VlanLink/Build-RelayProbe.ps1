[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$VlanSourceRoot,
    [Parameter(Mandatory=$true)][string]$NativeObjectDirectory,
    [string]$OutputDirectory=(Join-Path $PSScriptRoot ('bin\probe-'+[Guid]::NewGuid().ToString('N')))
)
$ErrorActionPreference='Stop'
$source=(Resolve-Path -LiteralPath $VlanSourceRoot).Path
$objects=(Resolve-Path -LiteralPath $NativeObjectDirectory).Path
$null=Get-Command cl,link -ErrorAction Stop
$objectNames=@('cli_net','monocypher','cli_peer','cli_fec','ikcp','cm256','gf256')
foreach($name in ($objectNames | ForEach-Object { $_+'.obj' })) {
    if(-not (Test-Path -LiteralPath (Join-Path $objects $name))) { throw 'Build the matching VLan CLI first.' }
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$out=(Resolve-Path -LiteralPath $OutputDirectory).Path
Push-Location -LiteralPath $out
try {
    & cl /nologo /O2 /EHsc /utf-8 /DWIN32_LEAN_AND_MEAN /DNOMINMAX /DUSE_SSSE3 /Dcm256cc_STATIC /D_CRT_SECURE_NO_WARNINGS /D_WINSOCK_DEPRECATED_NO_WARNINGS ('/I'+$source+'\client-cli\src') ('/I'+$source+'\common') ('/I'+$source+'\3rdparty\monocypher') ('/I'+$source+'\3rdparty\kcp') ('/I'+$source+'\3rdparty\cm256cc') ('/FI'+$source+'\common\msvc2015_compat.h') /c (Join-Path $PSScriptRoot 'HeadlessRelayProbe.cpp') /Fo:HeadlessRelayProbe.obj
    if($LASTEXITCODE -ne 0) { throw 'Headless probe compilation failed.' }
    $nativeObjects=$objectNames | ForEach-Object { Join-Path $objects ($_+'.obj') }
    & link /nologo /OUT:HeadlessRelayProbe.exe HeadlessRelayProbe.obj @nativeObjects ws2_32.lib bcrypt.lib
    if($LASTEXITCODE -ne 0) { throw 'Headless probe linking failed.' }
    Write-Output (Join-Path $out 'HeadlessRelayProbe.exe')
} finally { Pop-Location }
