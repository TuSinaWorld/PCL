[CmdletBinding()]
param(
    [string]$InputPath,
    [string]$OutputPath
)

# This script receives a deliberately selected build input. It never discovers a
# local configuration file or reads credential environment variables.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:failureCode = 'Input: unexpected-error'
$temporaryPath = $null
$inputStream = $null

function Stop-ConfigBuild([string]$Code) {
    $script:failureCode = $Code
    throw [InvalidOperationException]::new('Embedded configuration validation failed.')
}

function Assert-NoReparsePoint([string]$Path, [string]$Field) {
    # Reject junctions/symlinks so two spelled paths cannot overwrite the input
    # through a shared target. The open input handle also denies deletion for
    # the duration of the build, covering other aliases of the same file.
    $candidate = $Path
    while (-not [string]::IsNullOrEmpty($candidate)) {
        if ([IO.File]::Exists($candidate) -or [IO.Directory]::Exists($candidate)) {
            if (([IO.File]::GetAttributes($candidate) -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                Stop-ConfigBuild ($Field + ': reparse-point-not-supported')
            }
        }
        $candidate = [IO.Path]::GetDirectoryName($candidate)
    }
}

function Assert-NoDuplicateFields([string]$Json) {
    # ConvertFrom-Json on Windows PowerShell can silently keep the final copy of
    # a duplicate key. Scan only object-level key tokens, after JSON validation,
    # so escaped spellings and case variants cannot make the allowlist ambiguous.
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $depth = 0
    for ($index = 0; $index -lt $Json.Length; $index++) {
        $character = $Json[$index]
        if ($character -eq '"') {
            $start = $index
            $index++
            while ($index -lt $Json.Length) {
                if ($Json[$index] -eq '\') {
                    $index += 2
                    continue
                }
                if ($Json[$index] -eq '"') { break }
                $index++
            }
            if ($depth -eq 1) {
                $next = $index + 1
                while ($next -lt $Json.Length -and [char]::IsWhiteSpace($Json[$next])) { $next++ }
                if ($next -lt $Json.Length -and $Json[$next] -eq ':') {
                    $name = ConvertFrom-Json -InputObject $Json.Substring($start, $index - $start + 1) -ErrorAction Stop
                    if (-not $names.Add($name)) { Stop-ConfigBuild 'Input: duplicate-field' }
                }
            }
        } elseif ($character -eq '{' -or $character -eq '[') {
            $depth++
        } elseif ($character -eq '}' -or $character -eq ']') {
            $depth--
        }
    }
}

function Get-AllowlistedValue($Configuration, [string]$Name, [string]$Alias) {
    $found = @($Configuration.PSObject.Properties | Where-Object {
        [string]::Equals($_.Name, $Name, [StringComparison]::Ordinal) -or
        [string]::Equals($_.Name, $Alias, [StringComparison]::Ordinal)
    })
    if ($found.Count -eq 0) { Stop-ConfigBuild ($Name + ': missing') }
    if ($found.Count -ne 1) { Stop-ConfigBuild ($Name + ': conflicting-fields') }
    $value = $found[0].Value
    if ($value -isnot [string]) { Stop-ConfigBuild ($Name + ': string-required') }
    if ([string]::IsNullOrWhiteSpace($value)) { Stop-ConfigBuild ($Name + ': blank') }
    foreach ($character in $value.ToCharArray()) {
        if ([char]::IsControl($character)) { Stop-ConfigBuild ($Name + ': control-character') }
    }
    return $value
}

try {
    if ([string]::IsNullOrWhiteSpace($InputPath)) { Stop-ConfigBuild 'InputPath: required' }
    if ([string]::IsNullOrWhiteSpace($OutputPath)) { Stop-ConfigBuild 'OutputPath: required' }
    try {
        $inputFullPath = [IO.Path]::GetFullPath($InputPath)
        $outputFullPath = [IO.Path]::GetFullPath($OutputPath)
    } catch {
        Stop-ConfigBuild 'Path: invalid'
    }
    if ([string]::Equals($inputFullPath, $outputFullPath, [StringComparison]::OrdinalIgnoreCase)) {
        Stop-ConfigBuild 'OutputPath: same-as-input'
    }
    $outputDirectory = [IO.Path]::GetDirectoryName($outputFullPath)
    $insideObj = $false
    $ancestor = $outputDirectory
    while (-not [string]::IsNullOrEmpty($ancestor)) {
        if ([string]::Equals([IO.Path]::GetFileName($ancestor), 'obj', [StringComparison]::OrdinalIgnoreCase)) {
            $insideObj = $true
            break
        }
        $ancestor = [IO.Path]::GetDirectoryName($ancestor)
    }
    if (-not $insideObj) { Stop-ConfigBuild 'OutputPath: obj-directory-required' }
    if (-not [string]::Equals([IO.Path]::GetExtension($outputFullPath), '.json', [StringComparison]::OrdinalIgnoreCase)) {
        Stop-ConfigBuild 'OutputPath: json-extension-required'
    }
    if (-not [IO.File]::Exists($inputFullPath)) { Stop-ConfigBuild 'InputPath: file-not-found' }
    $script:failureCode = 'InputPath: read-failed'
    Assert-NoReparsePoint $inputFullPath 'InputPath'
    Assert-NoReparsePoint $outputFullPath 'OutputPath'

    # Hold a read-only handle while checking size and reading. No unbounded read
    # or allocation is allowed, even if another process tries to grow the file.
    $inputStream = [IO.File]::Open($inputFullPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    if ($inputStream.Length -gt 1048576) { Stop-ConfigBuild 'Input: exceeds-1-MiB' }
    $inputBytes = [byte[]]::new([int]$inputStream.Length)
    $offset = 0
    while ($offset -lt $inputBytes.Length) {
        $read = $inputStream.Read($inputBytes, $offset, $inputBytes.Length - $offset)
        if ($read -eq 0) { Stop-ConfigBuild 'Input: incomplete-read' }
        $offset += $read
    }
    if ($inputStream.ReadByte() -ne -1) { Stop-ConfigBuild 'Input: exceeds-1-MiB' }
    try {
        $utf8 = [Text.UTF8Encoding]::new($false, $true)
        $jsonText = $utf8.GetString($inputBytes)
        if ($jsonText.Length -gt 0 -and $jsonText[0] -eq [char]0xFEFF) { $jsonText = $jsonText.Substring(1) }
    } catch {
        Stop-ConfigBuild 'Input: invalid-utf8'
    }
    try {
        $configuration = ConvertFrom-Json -InputObject $jsonText -ErrorAction Stop
    } catch {
        Stop-ConfigBuild 'Input: invalid-json'
    }
    # Windows PowerShell may wrap even an empty JSON array as PSCustomObject.
    # The validated root token is therefore checked as well as the CLR type.
    if (-not $jsonText.TrimStart().StartsWith('{', [StringComparison]::Ordinal) -or
        $configuration -isnot [pscustomobject]) { Stop-ConfigBuild 'Input: object-required' }
    Assert-NoDuplicateFields $jsonText
    $clientId = Get-AllowlistedValue $configuration 'OAuthClientId' 'PCL_MS_CLIENT_ID'
    $apiKey = Get-AllowlistedValue $configuration 'CurseForgeAPIKey' 'PCL_CURSEFORGE_API_KEY'
    $parsedClientId = [Guid]::Empty
    if (-not [Guid]::TryParseExact($clientId, 'D', [ref]$parsedClientId) -or $parsedClientId -eq [Guid]::Empty) {
        Stop-ConfigBuild 'OAuthClientId: invalid-guid'
    }
    # Construct a new object, never serialize the input object. Unknown fields,
    # including every CDN-signing field and nested object, are discarded.
    $normalizedJson = [ordered]@{
        OAuthClientId = $parsedClientId.ToString('D')
        CurseForgeAPIKey = $apiKey
    } | ConvertTo-Json -Compress -Depth 2
    $outputBytes = $utf8.GetBytes($normalizedJson + "`n")

    $script:failureCode = 'OutputPath: write-failed'
    [IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
    Assert-NoReparsePoint $outputFullPath 'OutputPath'
    $unchanged = $false
    if ([IO.File]::Exists($outputFullPath)) {
        $existingInfo = [IO.FileInfo]::new($outputFullPath)
        if ($existingInfo.Length -eq $outputBytes.Length) {
            $existingBytes = [IO.File]::ReadAllBytes($outputFullPath)
            $unchanged = $true
            for ($index = 0; $index -lt $outputBytes.Length; $index++) {
                if ($existingBytes[$index] -ne $outputBytes[$index]) { $unchanged = $false; break }
            }
        }
    }
    if (-not $unchanged) {
        $temporaryPath = [IO.Path]::Combine($outputDirectory, '.embedded-config-' + [Guid]::NewGuid().ToString('N') + '.tmp')
        $outputStream = $null
        try {
            $outputStream = [IO.File]::Open($temporaryPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
            $outputStream.Write($outputBytes, 0, $outputBytes.Length)
            $outputStream.Flush($true)
        } finally {
            if ($null -ne $outputStream) { $outputStream.Dispose() }
        }
        if ([IO.File]::Exists($outputFullPath)) {
            # PowerShell coerces $null to an empty string for this overload.
            [IO.File]::Replace($temporaryPath, $outputFullPath, [System.Management.Automation.Language.NullString]::Value, $true)
        } else {
            [IO.File]::Move($temporaryPath, $outputFullPath)
        }
        $temporaryPath = $null
    }
} catch {
    # Exception messages can include JSON fragments, paths, or supplied values.
    # Print only a static validation code and never the caught error object.
    [Console]::Error.WriteLine('EmbeddedClientConfig: ' + $script:failureCode)
    exit 1
} finally {
    if ($null -ne $inputStream) { $inputStream.Dispose() }
    if ($null -ne $temporaryPath -and [IO.File]::Exists($temporaryPath)) {
        try { [IO.File]::Delete($temporaryPath) } catch { }
    }
}
