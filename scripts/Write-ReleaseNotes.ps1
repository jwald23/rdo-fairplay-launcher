param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory)][string]$BuildUrl,
    [Parameter(Mandatory)][string]$AttestationUrl
)
$ErrorActionPreference = 'Stop'
$folder = Join-Path (Join-Path $PSScriptRoot '..') "artifacts/releases/$Version"
$evidence = Get-Content (Join-Path $folder 'DOWNLOAD-EVIDENCE.json') -Raw | ConvertFrom-Json
$installer = @($evidence.files | Where-Object file -eq 'RDOFairPlay-Setup-Windows-x64.exe')
if ($installer.Count -ne 1) { throw 'Installer evidence is missing or ambiguous.' }
$changes = Get-Content (Join-Path $folder 'CHANGES.md') -Raw
if (-not $changes.Trim()) { throw 'Release changes are required.' }
$signature = if ($installer[0].authenticode -eq 'NotSigned') { 'Unsigned' } else { $installer[0].authenticode }
$notes = @"
## Changes

$($changes.Trim())

**Install or update:** Download **RDOFairPlay-Setup-Windows-x64.exe** below and run it. Close the launcher first if updating. Windows 10/11 x64; .NET is included.

**Checks:** [Build & tests]($BuildUrl) passed · [Build provenance]($AttestationUrl) verified · [VirusTotal]($($installer[0].virusTotalReport)): scan status not yet verified for this file.

Windows signature: $signature. [Verification help](https://github.com/jwald23/rdo-fairplay-launcher/blob/main/docs/DOWNLOAD_VERIFICATION.md).

<details>
<summary>Installer SHA256</summary>

``$($installer[0].sha256)``

</details>
"@
$notes | Set-Content (Join-Path $folder 'RELEASE-NOTES.md') -Encoding utf8NoBOM
