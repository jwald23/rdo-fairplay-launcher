param([Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$release = Join-Path $root "artifacts/releases/$Version"
$package = Join-Path $release 'RDOFairPlay'
$compiler = @((Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'), (Join-Path $env:ProgramFiles 'Inno Setup 6/ISCC.exe')) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $compiler) { throw 'Install Inno Setup 6 before building the installer.' }
if (-not (Test-Path -LiteralPath (Join-Path $package 'coreclr.dll'))) { throw 'Run Publish.ps1 first to create the self-contained package.' }
& $compiler "/DAppVersion=$Version" "/DPackageDir=$package" "/DReleaseDir=$release" (Join-Path $root 'launcher/installer/FairPlay.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
$files = @('RDOFairPlay-Windows-x64.zip', 'RDOFairPlay-Setup-Windows-x64.exe')
$checksums = foreach ($file in $files) { $hash = (Get-FileHash -LiteralPath (Join-Path $release $file) -Algorithm SHA256).Hash.ToLowerInvariant(); "$hash  $file" }
$checksums | Set-Content -LiteralPath (Join-Path $release 'SHA256SUMS.txt') -Encoding ascii
