param([Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version)
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or -not $env:RUNNER_TEMP) { throw 'Run installer lifecycle tests only on a disposable GitHub Actions runner.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$setup = Join-Path $root "artifacts/releases/$Version/RDOFairPlay-Setup-Windows-x64.exe"
$install = Join-Path $env:RUNNER_TEMP 'fairplay-installer-test'
$data = Join-Path $env:LOCALAPPDATA 'CommunityFrontier'
if ((Test-Path -LiteralPath $install) -or (Test-Path -LiteralPath $data)) { throw 'Installer tests require a fresh runner without an existing FairPlay installation or user data.' }
function RunProcess([string]$File, [string[]]$Arguments) {
    $process = Start-Process -FilePath $File -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(120000)) { throw 'Installer test process timed out.' }
    return $process.ExitCode
}
$setupArgs = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/DIR="' + $install + '"'))
if ((RunProcess $setup $setupArgs) -ne 0) { throw 'Clean install failed.' }
$exe = Join-Path $install 'RDOFairPlay.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Installed executable missing.' }
if ((RunProcess $exe @('--render-preview', '--small')) -ne 0) { throw 'Installed launcher failed startup.' }
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'RDO FairPlay.lnk'
if (-not (Test-Path -LiteralPath $shortcut)) { throw 'Start menu shortcut missing.' }
New-Item -ItemType Directory -Path (Join-Path $data 'Backups') -Force | Out-Null
$sentinel = Join-Path $data 'Backups/installer-test.txt'
Set-Content -LiteralPath $sentinel -Value 'Preserve recovery files'
if ((RunProcess $setup $setupArgs) -ne 0) { throw 'Upgrade/reinstall failed.' }
if ((Get-Content -LiteralPath $sentinel -Raw).Trim() -ne 'Preserve recovery files') { throw 'Upgrade changed recovery data.' }
$uninstall = Join-Path $install 'unins000.exe'
$uninstallArgs = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
New-Item -ItemType Directory -Path (Join-Path $data 'State') -Force | Out-Null
$badJournal = Join-Path $data 'State/installer-test.json'
Set-Content -LiteralPath $badJournal -Value 'invalid recovery journal'
if ((RunProcess $uninstall $uninstallArgs) -eq 0 -or -not (Test-Path -LiteralPath $exe)) { throw 'Uninstaller failed to preserve the app after a recovery error.' }
Remove-Item -LiteralPath $badJournal
if ((RunProcess $uninstall $uninstallArgs) -ne 0) { throw 'Uninstall failed after recovery issue was resolved.' }
if ((Test-Path -LiteralPath $exe) -or (Test-Path -LiteralPath $shortcut)) { throw 'Uninstall left executable or shortcut behind.' }
if ((Get-Content -LiteralPath $sentinel -Raw).Trim() -ne 'Preserve recovery files') { throw 'Uninstall changed preserved recovery data.' }
Write-Host 'Installer lifecycle passed: install, launch, shortcut, upgrade, recovery guard, uninstall, retained user data.'
