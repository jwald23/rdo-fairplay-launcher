param([ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.1.0')
$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    $releaseRoot = Join-Path (Get-Location) "artifacts/releases/$Version"
    $package = Join-Path $releaseRoot 'RDOFairPlay'
    if (Test-Path -LiteralPath $releaseRoot) { throw 'Release output already exists. Use a new version or a clean checkout.' }
    New-Item -ItemType Directory -Path $package -Force | Out-Null
    dotnet publish launcher/CommunityFrontier.Launcher -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false "-p:Version=$Version" -o $package
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    $settings = Get-Content (Join-Path $package 'launcher.settings.json') -Raw | ConvertFrom-Json
    if ($settings.BackendUrl -ne 'https://api.rdofairplay.com' -or $settings.CommunityId -ne 'aabd5baf-ae9c-4ec9-81c5-86de5cdf8fdb' -or $settings.DevelopmentLobbyIdentifier) { throw 'Release must use production FairPlay configuration.' }
    foreach ($required in @('RDOFairPlay.exe', 'coreclr.dll', 'hostfxr.dll', 'PresentationFramework.dll', 'THIRD_PARTY_LICENSE.txt')) {
        if (-not (Test-Path -LiteralPath (Join-Path $package $required))) { throw "Missing release file: $required" }
    }
    @"
RDO FairPlay $Version - Windows x64

Extract the entire ZIP into a folder, then open RDOFairPlay.exe.
The .NET runtime is included. No SDK or separate runtime installation is needed.
Sign in with Discord and submit your Red Dead name for Support approval.
Original Settings remains available while verification is pending.

To update: close the launcher, extract the new version to a new folder, and run it.
Your saved game location, sign-in and recovery files remain in LocalAppData/CommunityFrontier.
Do not delete your recovery files while a private configuration is active.

This portable build is not code-signed and does not auto-update.
Support: https://discord.gg/Mp6skUnf2b
"@ | Set-Content (Join-Path $package 'START-HERE.txt') -Encoding utf8NoBOM
    $archive = Join-Path $releaseRoot 'RDOFairPlay-Windows-x64.zip'
    Compress-Archive -Path $package -DestinationPath $archive -CompressionLevel Optimal
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  RDOFairPlay-Windows-x64.zip" | Set-Content (Join-Path $releaseRoot 'SHA256SUMS.txt') -Encoding ascii
    Write-Host "Ready-to-run download: $archive"
} finally { Pop-Location }
