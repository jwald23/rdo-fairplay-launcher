param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{40}$')][string]$Commit,
    [Parameter(Mandatory)][ValidatePattern('^https://github.com/jwald23/rdo-fairplay-launcher/actions/runs/\d+$')][string]$BuildUrl
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$folder = Join-Path $root "artifacts/releases/$Version"
$project = Join-Path $root 'launcher/CommunityFrontier.Launcher/CommunityFrontier.Launcher.csproj'
$auditText = & dotnet list $project package --vulnerable --include-transitive --format json --output-version 1
if ($LASTEXITCODE -ne 0) { throw 'Dependency advisory check failed. No clean result will be published.' }
$audit = ($auditText -join "`n") | ConvertFrom-Json
if (-not $audit.projects -or $audit.problems -or @($audit.projects | Where-Object problems).Count) { throw 'Dependency advisory report is incomplete.' }
$vulnerable = @($audit.projects | ForEach-Object { $_.frameworks } | ForEach-Object { @($_.topLevelPackages) + @($_.transitivePackages) } | Where-Object { $_ -and $_.vulnerabilities })
$auditText | Set-Content (Join-Path $folder 'DEPENDENCY-AUDIT.json') -Encoding utf8NoBOM
$records = foreach ($name in @('RDOFairPlay-Setup-Windows-x64.exe', 'RDOFairPlay-Windows-x64.zip')) {
    $file = Get-Item -LiteralPath (Join-Path $folder $name)
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $signature = if ($name.EndsWith('.exe')) { (Get-AuthenticodeSignature -LiteralPath $file.FullName).Status.ToString() } else { 'NotApplicable' }
    [ordered]@{ file = $name; bytes = $file.Length; sha256 = $hash; authenticode = $signature; virusTotalReport = "https://www.virustotal.com/gui/file/$hash/detection"; virusTotalStatus = 'Not checked by this workflow; open the report to check availability and results.' }
}
$generated = [DateTime]::UtcNow.ToString('o')
[ordered]@{ version = $Version; generatedAtUtc = $generated; sourceCommit = $Commit; buildUrl = $BuildUrl; dependencyAudit = @{ vulnerablePackageEntries = $vulnerable.Count; report = 'DEPENDENCY-AUDIT.json'; scope = 'Launcher direct and transitive NuGet packages only; excludes bundled .NET runtime, installer tooling and application behavior.' }; files = @($records) } |
    ConvertTo-Json -Depth 8 | Set-Content (Join-Path $folder 'DOWNLOAD-EVIDENCE.json') -Encoding utf8NoBOM
$lines = @(
    "# Download evidence: RDO FairPlay $Version", '',
    "Generated: $generated. This is the evidence-generation time, not a VirusTotal scan time.", '',
    "[Source commit](https://github.com/jwald23/rdo-fairplay-launcher/commit/$Commit) · [Build, tests and provenance verification]($BuildUrl)", '',
    '| File | Bytes | Windows signature | VirusTotal |', '| --- | ---: | --- | --- |'
)
foreach ($r in $records) { $lines += "| $($r.file) | $($r.bytes) | $($r.authenticode) | [Open hash-specific report]($($r.virusTotalReport)) |" }
$lines += @('', '## SHA256', '')
foreach ($r in $records) { $lines += "- $($r.file): ``$($r.sha256)``" }
$lines += @('', '## Known dependency advisories', '',
    "$($vulnerable.Count) vulnerable package entries reported for direct and transitive launcher NuGet dependencies. See DEPENDENCY-AUDIT.json for the underlying data and advisory sources.",
    'This check does not cover the bundled .NET runtime, installer tooling, unknown vulnerabilities or application behavior.', '',
    '## Read the evidence accurately', '',
    '- VirusTotal links identify these exact files. A link alone is not a completed scan: Item not found means no report is available. Check the report date, SHA256 and all engine results. Errors, timeouts and unsupported file types are not clean verdicts.',
    '- Results for another version or the portable ZIP do not cover this installer. A ZIP report may have limited archive coverage.',
    '- A zero-detection result is additional evidence, not proof of safety. No independent security audit is claimed.',
    '- GitHub provenance verifies build origin and integrity; it is separate from Windows code signing and malware analysis.',
    '- Keep antivirus enabled. Report detections with the release version and detection name; do not bypass or dismiss them.')
$lines | Set-Content (Join-Path $folder 'DOWNLOAD-EVIDENCE.md') -Encoding utf8NoBOM
Write-Host "Download evidence generated. Vulnerable dependency entries: $($vulnerable.Count)"
