[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$scriptPath = Join-Path $repoRoot 'build\Prepare-EmbeddedClientConfig.ps1'
$runtimeShell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
if (-not (Test-Path -LiteralPath $runtimeShell -PathType Leaf)) { throw 'Windows PowerShell is required.' }
$runRoot = Join-Path $PSScriptRoot ('bin\run-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($runRoot) | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false)
$fixtureClientId = 'a1111111-b222-4333-8444-55555555555c'
$fixtureKey = 'SYNTHETIC_CF_ONLY_do_not_use_91d13c'
$fixtureCdn = 'SYNTHETIC_CDN_MUST_BE_FILTERED_c0699b'
$checks = 0

function Assert-Check([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:checks++
}

function Write-Fixture([string]$Name, [string]$Text) {
    $path = Join-Path $runRoot ($Name + '.json')
    [IO.File]::WriteAllText($path, $Text, $utf8)
    return $path
}

function Invoke-Preparation([string]$InputFile, [string]$OutputFile, [switch]$OmitInput, [switch]$OmitOutput) {
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $runtimeShell
    $arguments = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + $scriptPath + '"'
    if (-not $OmitInput) { $arguments += ' -InputPath "' + $InputFile + '"' }
    if (-not $OmitOutput) { $arguments += ' -OutputPath "' + $OutputFile + '"' }
    $startInfo.Arguments = $arguments
    $startInfo.WorkingDirectory = $runRoot
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    # Tests never inspect or forward inherited credentials. Only the operating
    # system variables needed by Windows PowerShell are copied into the child.
    $startInfo.EnvironmentVariables.Clear()
    foreach ($name in @('SystemRoot', 'WINDIR', 'SystemDrive', 'ComSpec', 'PATHEXT', 'OS', 'TEMP', 'TMP',
                         'USERPROFILE', 'HOMEDRIVE', 'HOMEPATH', 'APPDATA', 'LOCALAPPDATA',
                         'ProgramData', 'ProgramFiles', 'ProgramFiles(x86)', 'ProgramW6432',
                         'PUBLIC', 'PROCESSOR_ARCHITECTURE', 'NUMBER_OF_PROCESSORS')) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if ($null -ne $value) { $startInfo.EnvironmentVariables[$name] = $value }
    }
    $startInfo.EnvironmentVariables['PATH'] = (Join-Path $env:WINDIR 'System32') + ';' + (Split-Path $runtimeShell -Parent)
    $process = [Diagnostics.Process]::Start($startInfo)
    try {
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        return [pscustomobject]@{ ExitCode = $process.ExitCode; Log = $stdoutTask.Result + $stderrTask.Result }
    } finally {
        $process.Dispose()
    }
}

function Assert-NoValueLog($Result, [string[]]$AdditionalValues = @()) {
    foreach ($value in (@($fixtureClientId, $fixtureKey, $fixtureCdn) + $AdditionalValues)) {
        if (-not [string]::IsNullOrEmpty($value)) {
            Assert-Check (-not $Result.Log.Contains($value)) 'A fixture value appeared in a build log.'
        }
    }
}

function Assert-Rejected([string]$Name, [string]$Text, [string]$Code, [string[]]$AdditionalValues = @()) {
    $inputFile = Write-Fixture $Name $Text
    $outputFile = Join-Path $runRoot ('obj\rejected\' + $Name + '.json')
    $result = Invoke-Preparation $inputFile $outputFile
    Assert-Check ($result.ExitCode -ne 0) ('Invalid fixture was accepted: ' + $Name)
    Assert-Check ($result.Log.Contains($Code)) ('Wrong validation code for fixture: ' + $Name)
    Assert-Check (-not [IO.File]::Exists($outputFile)) ('Invalid fixture produced a resource: ' + $Name)
    Assert-NoValueLog $result $AdditionalValues
}

$valid = [ordered]@{
    OAuthClientId = $fixtureClientId.ToUpperInvariant()
    CurseForgeAPIKey = $fixtureKey
    CdnKey = $fixtureCdn
    CdnSecret = $fixtureCdn
    PCL_CDN_KEY = $fixtureCdn
    Unknown = [ordered]@{ OAuthClientId = 'nested-untrusted'; CdnSecret = $fixtureCdn }
}
$validInput = Write-Fixture 'valid' ($valid | ConvertTo-Json -Depth 5)
$validOutput = Join-Path $runRoot 'obj\nested\PCL.EmbeddedClientConfig.json'
$result = Invoke-Preparation $validInput $validOutput
Assert-Check ($result.ExitCode -eq 0) 'Valid fixture failed.'
Assert-Check ([IO.File]::Exists($validOutput)) 'Resource was not written.'
Assert-NoValueLog $result
$outputBytes = [IO.File]::ReadAllBytes($validOutput)
Assert-Check (-not ($outputBytes.Length -ge 3 -and $outputBytes[0] -eq 0xEF -and $outputBytes[1] -eq 0xBB -and $outputBytes[2] -eq 0xBF)) 'Output has a UTF-8 BOM.'
$outputText = [IO.File]::ReadAllText($validOutput)
$output = ConvertFrom-Json -InputObject $outputText
Assert-Check (@($output.PSObject.Properties).Count -eq 2) 'Resource contains non-allowlisted fields.'
Assert-Check ($output.OAuthClientId -ceq $fixtureClientId) 'Client ID was not normalized.'
Assert-Check ($output.CurseForgeAPIKey -ceq $fixtureKey) 'API key changed.'
Assert-Check (-not $outputText.Contains($fixtureCdn)) 'CDN fixture leaked into resource.'
$originalInputHash = (Get-FileHash -LiteralPath $validInput -Algorithm SHA256).Hash
$fixedTimestamp = [DateTime]::SpecifyKind([DateTime]'2020-01-02T03:04:05', [DateTimeKind]::Utc)
[IO.File]::SetLastWriteTimeUtc($validOutput, $fixedTimestamp)
$result = Invoke-Preparation $validInput $validOutput
Assert-Check ($result.ExitCode -eq 0) 'Repeated preparation failed.'
Assert-Check ([IO.File]::GetLastWriteTimeUtc($validOutput) -eq $fixedTimestamp) 'Identical output was rewritten.'
Assert-Check ((Get-FileHash -LiteralPath $validInput -Algorithm SHA256).Hash -eq $originalInputHash) 'Input was modified.'
Assert-NoValueLog $result

# Formatting, field order, GUID casing and ignored-field changes do not change
# the normalized resource or its timestamp. An allowlisted value change does.
$normalizationInput = Write-Fixture 'normalization-only' (@{
    CurseForgeAPIKey = $fixtureKey
    OAuthClientId = $fixtureClientId
    CdnKey = 'SYNTHETIC_DIFFERENT_IGNORED_CDN'
    AdditionalUnknownField = @('unknown', 'ignored')
} | ConvertTo-Json -Compress)
$result = Invoke-Preparation $normalizationInput $validOutput
Assert-Check ($result.ExitCode -eq 0) 'Normalization-only input change failed.'
Assert-Check ([IO.File]::ReadAllText($validOutput) -ceq $outputText) 'Normalization-only change altered output.'
Assert-Check ([IO.File]::GetLastWriteTimeUtc($validOutput) -eq $fixedTimestamp) 'Normalization-only change rewrote output.'
Assert-NoValueLog $result @('SYNTHETIC_DIFFERENT_IGNORED_CDN')
$changedKey = 'SYNTHETIC_CHANGED_CF_VALUE_6a25bf'
$changedInput = Write-Fixture 'changed-allowlisted' (@{ OAuthClientId = $fixtureClientId; CurseForgeAPIKey = $changedKey } | ConvertTo-Json)
$result = Invoke-Preparation $changedInput $validOutput
Assert-Check ($result.ExitCode -eq 0) 'Allowlisted input change failed.'
$changedOutput = ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($validOutput))
Assert-Check ($changedOutput.CurseForgeAPIKey -ceq $changedKey) 'Allowlisted input change was not written.'
Assert-Check ([IO.File]::GetLastWriteTimeUtc($validOutput) -ne $fixedTimestamp) 'Allowlisted input change did not replace output.'
Assert-NoValueLog $result @($changedKey)

$aliasInput = Write-Fixture 'aliases' (@{ PCL_MS_CLIENT_ID = $fixtureClientId; PCL_CURSEFORGE_API_KEY = $fixtureKey; PCL_CDN_KEY = $fixtureCdn } | ConvertTo-Json)
$aliasOutput = Join-Path $runRoot 'obj\aliases.json'
$result = Invoke-Preparation $aliasInput $aliasOutput
Assert-Check ($result.ExitCode -eq 0) 'Aliases were rejected.'
Assert-Check ([IO.File]::ReadAllText($aliasOutput) -ceq $outputText) 'Aliases do not produce canonical output.'
Assert-NoValueLog $result

# This is data, including PowerShell-looking text, JSON-looking text, quotes,
# backslashes and Unicode. It must survive JSON escaping without executing.
$injectionMarker = Join-Path $runRoot 'injection-executed.txt'
$escapedKey = 'SYNTHETIC_"\$(Set-Content -LiteralPath ''' + $injectionMarker + ''' -Value executed); ${env:PATH}; ' + [char]0x96EA + '; {"CdnKey":"injected"}'
$escapeInput = Write-Fixture 'escaping' (@{ OAuthClientId = $fixtureClientId; CurseForgeAPIKey = $escapedKey } | ConvertTo-Json)
$escapeOutput = Join-Path $runRoot 'obj\escaping.json'
$result = Invoke-Preparation $escapeInput $escapeOutput
Assert-Check ($result.ExitCode -eq 0) 'Escaped fixture was rejected.'
$escapedOutput = ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($escapeOutput))
Assert-Check ($escapedOutput.CurseForgeAPIKey -ceq $escapedKey) 'Escaped fixture did not round-trip.'
Assert-Check (@($escapedOutput.PSObject.Properties).Count -eq 2) 'Escaped fixture injected a field.'
Assert-Check (-not [IO.File]::Exists($injectionMarker)) 'Fixture text was executed.'
Assert-NoValueLog $result @($escapedKey)

$base = @{ OAuthClientId = $fixtureClientId; CurseForgeAPIKey = $fixtureKey }
foreach ($field in @('OAuthClientId', 'CurseForgeAPIKey')) {
    foreach ($invalid in @(
        @{ Name = 'null'; Value = $null; Code = 'string-required' },
        @{ Name = 'number'; Value = 42; Code = 'string-required' },
        @{ Name = 'boolean'; Value = $true; Code = 'string-required' },
        @{ Name = 'array'; Value = @('synthetic-value'); Code = 'string-required' },
        @{ Name = 'object'; Value = @{ value = 'synthetic-value' }; Code = 'string-required' },
        @{ Name = 'empty'; Value = ''; Code = 'blank' },
        @{ Name = 'whitespace'; Value = '   '; Code = 'blank' },
        @{ Name = 'control'; Value = "synthetic`r`nvalue"; Code = 'control-character' },
        @{ Name = 'nul'; Value = ('synthetic' + [char]0 + 'value'); Code = 'control-character' }
    )) {
        $fixture = $base.Clone()
        $fixture[$field] = $invalid.Value
        Assert-Rejected ($field + '-' + $invalid.Name) ($fixture | ConvertTo-Json -Depth 5) ($field + ': ' + $invalid.Code)
    }
    $fixture = $base.Clone()
    $fixture.Remove($field)
    Assert-Rejected ($field + '-missing') ($fixture | ConvertTo-Json) ($field + ': missing')
}
Assert-Rejected 'invalid-guid' (@{ OAuthClientId = 'SYNTHETIC_NOT_A_GUID'; CurseForgeAPIKey = $fixtureKey } | ConvertTo-Json) 'OAuthClientId: invalid-guid' @('SYNTHETIC_NOT_A_GUID')
Assert-Rejected 'empty-guid' (@{ OAuthClientId = [Guid]::Empty.ToString(); CurseForgeAPIKey = $fixtureKey } | ConvertTo-Json) 'OAuthClientId: invalid-guid'
Assert-Rejected 'conflicting-aliases' (@{ OAuthClientId = $fixtureClientId; PCL_MS_CLIENT_ID = $fixtureClientId; CurseForgeAPIKey = $fixtureKey } | ConvertTo-Json) 'OAuthClientId: conflicting-fields'
Assert-Rejected 'conflicting-key-aliases' (@{ OAuthClientId = $fixtureClientId; CurseForgeAPIKey = $fixtureKey; PCL_CURSEFORGE_API_KEY = $fixtureKey } | ConvertTo-Json) 'CurseForgeAPIKey: conflicting-fields'
Assert-Rejected 'duplicate-key' ('{"OAuthClientId":"' + $fixtureClientId + '","OAuthClientId":"' + $fixtureClientId + '","CurseForgeAPIKey":"' + $fixtureKey + '"}') 'Input: duplicate-field'
Assert-Rejected 'escaped-duplicate-key' ('{"OAuthClientId":"' + $fixtureClientId + '","\u004fAuthClientId":"' + $fixtureClientId + '","CurseForgeAPIKey":"' + $fixtureKey + '"}') 'Input: duplicate-field'
Assert-Rejected 'malformed' ('{"OAuthClientId":"' + $fixtureClientId + '","CurseForgeAPIKey":"' + $fixtureKey + '", broken}') 'Input: invalid-json'
Assert-Rejected 'array-root' '[]' 'Input: object-required'
Assert-Rejected 'string-root' '"SYNTHETIC_ROOT_VALUE"' 'Input: object-required' @('SYNTHETIC_ROOT_VALUE')
Assert-Rejected 'null-root' 'null' 'Input: object-required'
Assert-Rejected 'oversized' (' ' * 1048577) 'Input: exceeds-1-MiB'
$boundaryJson = $base | ConvertTo-Json -Compress
$boundaryInput = Write-Fixture 'one-MiB' ($boundaryJson + (' ' * (1048576 - $utf8.GetByteCount($boundaryJson))))
$boundaryOutput = Join-Path $runRoot 'obj\one-MiB.json'
$result = Invoke-Preparation $boundaryInput $boundaryOutput
Assert-Check ($result.ExitCode -eq 0) 'An exactly 1 MiB valid input was rejected.'
Assert-Check ([IO.File]::ReadAllText($boundaryOutput) -ceq $outputText) 'Size-boundary fixture changed normalized output.'
Assert-NoValueLog $result

$invalidEncodingInput = Join-Path $runRoot 'invalid-utf8.json'
[IO.File]::WriteAllBytes($invalidEncodingInput, [byte[]]@(0xC3, 0x28))
$invalidEncodingOutput = Join-Path $runRoot 'obj\invalid-utf8.json'
$result = Invoke-Preparation $invalidEncodingInput $invalidEncodingOutput
Assert-Check ($result.ExitCode -ne 0 -and $result.Log.Contains('Input: invalid-utf8')) 'Invalid UTF-8 was accepted.'
Assert-Check (-not [IO.File]::Exists($invalidEncodingOutput)) 'Invalid UTF-8 produced a resource.'
Assert-NoValueLog $result

foreach ($missingInput in @($true, $false)) {
    $missingOutput = Join-Path $runRoot ('obj\missing-' + $missingInput + '.json')
    $result = Invoke-Preparation (Join-Path $runRoot 'does-not-exist.json') $missingOutput -OmitInput:$missingInput
    $code = if ($missingInput) { 'InputPath: required' } else { 'InputPath: file-not-found' }
    Assert-Check ($result.ExitCode -ne 0 -and $result.Log.Contains($code)) 'Missing input did not fail safely.'
    Assert-Check (-not [IO.File]::Exists($missingOutput)) 'Missing input produced a resource.'
    Assert-NoValueLog $result
}
$result = Invoke-Preparation $validInput '' -OmitOutput
Assert-Check ($result.ExitCode -ne 0 -and $result.Log.Contains('OutputPath: required')) 'Missing output did not fail safely.'
$result = Invoke-Preparation $validInput $validInput
Assert-Check ($result.ExitCode -ne 0 -and $result.Log.Contains('OutputPath: same-as-input')) 'Input overwrite was accepted.'
Assert-Check ((Get-FileHash -LiteralPath $validInput -Algorithm SHA256).Hash -eq $originalInputHash) 'Overwrite check changed input.'
$outsideOutput = Join-Path $runRoot 'outside-obj.json'
$result = Invoke-Preparation $validInput $outsideOutput
Assert-Check ($result.ExitCode -ne 0 -and $result.Log.Contains('OutputPath: obj-directory-required')) 'Non-obj output was accepted.'
Assert-Check (-not [IO.File]::Exists($outsideOutput)) 'Non-obj output was created.'

# Failed regeneration must retain the last valid resource and remove any temp.
$beforeFailure = [IO.File]::ReadAllText($validOutput)
$badRegeneration = Write-Fixture 'bad-regeneration' (@{ OAuthClientId = $fixtureClientId; CurseForgeAPIKey = @($fixtureKey) } | ConvertTo-Json)
$result = Invoke-Preparation $badRegeneration $validOutput
Assert-Check ($result.ExitCode -ne 0) 'Invalid regeneration was accepted.'
Assert-Check ([IO.File]::ReadAllText($validOutput) -ceq $beforeFailure) 'Invalid regeneration damaged the resource.'
Assert-NoValueLog $result
Assert-Check (@(Get-ChildItem -LiteralPath (Join-Path $runRoot 'obj') -Recurse -Filter '*.tmp' -File).Count -eq 0) 'Temporary resource files were left behind.'

Write-Output ('PASS: embedded configuration build checks (' + $checks + ' assertions); synthetic fixtures only, Windows PowerShell 5.1.')
