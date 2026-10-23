$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Validate-Boundaries.ps1')
& (Join-Path $PSScriptRoot 'Validate-Interop.ps1')
