[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$runtimeShell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
if (-not (Test-Path -LiteralPath $runtimeShell -PathType Leaf)) {
    throw 'Recovered checks require Windows PowerShell and the .NET Framework VB compiler.'
}
$outputRoot = Join-Path $PSScriptRoot 'bin'
$runRoot = Join-Path $outputRoot ('run-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null
$fixtureConfig = Join-Path $runRoot 'empty-config.json'
[IO.File]::WriteAllText($fixtureConfig, '{}', [Text.UTF8Encoding]::new($false))
$suites = @(
    'RecoveredUpdateServer\Run.ps1',
    'RecoveredConfiguration\Run.ps1',
    'RecoveredToolbox\Run-Tests.ps1'
    'LocalThemes\Run.ps1'
    'LocalModpacks\Run.ps1'
    'LocalLink\Run.ps1'
    'LocalLinkNavigation\Run.ps1'
    'LocalLinkInput\Run.ps1'
    'EmbeddedConfiguration\Run.ps1'
    'EmbeddedConfigurationBuild\Run.ps1'
    'VlanLink\Run.ps1'
)
$results = @()
foreach ($suite in $suites) {
    $scriptPath = Join-Path $PSScriptRoot $suite
    if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) { throw "Missing suite: $suite" }
    $startInfo = New-Object Diagnostics.ProcessStartInfo
    $startInfo.FileName = $runtimeShell
    $startInfo.Arguments = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + $scriptPath + '"'
    $startInfo.WorkingDirectory = $PSScriptRoot
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    # Start with an empty environment. Only Windows runtime/path variables are copied;
    # inherited PCL configuration and other API credentials are never read or forwarded.
    $startInfo.EnvironmentVariables.Clear()
    foreach ($name in @('SystemRoot', 'WINDIR', 'SystemDrive', 'ComSpec', 'PATHEXT', 'OS', 'TEMP', 'TMP',
                         'USERPROFILE', 'HOMEDRIVE', 'HOMEPATH', 'APPDATA', 'LOCALAPPDATA',
                         'ProgramData', 'ProgramFiles', 'ProgramFiles(x86)', 'ProgramW6432',
                         'PUBLIC', 'PROCESSOR_ARCHITECTURE', 'NUMBER_OF_PROCESSORS')) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if ($null -ne $value) { $startInfo.EnvironmentVariables[$name] = $value }
    }
    $startInfo.EnvironmentVariables['PATH'] = (Join-Path $env:WINDIR 'System32') + ';' + (Split-Path $runtimeShell -Parent)
    $startInfo.EnvironmentVariables['PCL_RECOVERED_CONFIG'] = $fixtureConfig
    Write-Output ("Running isolated suite: $suite")
    $process = [Diagnostics.Process]::Start($startInfo)
    try {
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $stdout = $stdoutTask.Result
        $stderr = $stderrTask.Result
        $exitCode = $process.ExitCode
    } finally {
        $process.Dispose()
    }
    $suiteName = Split-Path (Split-Path $scriptPath -Parent) -Leaf
    [IO.File]::WriteAllText((Join-Path $runRoot ($suiteName + '.stdout.log')), $stdout)
    [IO.File]::WriteAllText((Join-Path $runRoot ($suiteName + '.stderr.log')), $stderr)
    if ($stdout) { Write-Output $stdout.TrimEnd() }
    if ($stderr) { [Console]::Error.WriteLine($stderr.TrimEnd()) }
    $results += [pscustomobject][ordered]@{ Suite = $suite; ExitCode = $exitCode }
    $results | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $runRoot 'result.json') -Encoding utf8
    if ($exitCode -ne 0) { throw "Recovered suite failed (exit $exitCode): $suite" }
}
Write-Output 'PASS: all recovered and local-function suites exited 0. Detailed logs are in tests/bin and each suite bin directory.'
