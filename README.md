# RDO FairPlay

A Windows launcher for shared private Red Dead Online lobbies, with Discord sign-in and support-reviewed access.

## Download

Download **RDOFairPlay-Setup-Windows-x64.exe** from [the latest release](https://github.com/jwald23/rdo-fairplay-launcher/releases/latest). Run the installer, open FairPlay, and sign in with Discord. The .NET runtime is included. A portable ZIP is also available.

Join [RDO FairPlay on Discord](https://discord.gg/Mp6skUnf2b), submit your Red Dead Online username, and wait for Support approval. Fair Play unlocks once access is confirmed. Original Settings remains available while you wait.

## Updating and uninstalling

Close the launcher and run the newer installer to update. Uninstall restores managed game configuration first and stops if recovery fails. Your settings and recovery files remain under `%LocalAppData%\CommunityFrontier`.

The installer is currently unsigned. Release assets include SHA256 checksums. Downloads do not update themselves automatically.

## Build

Windows, the .NET 10 SDK, and PowerShell 7 are required. Run `scripts/Verify.ps1` to build and test. Run `scripts/Publish.ps1 -Version 0.2.0` to package the self-contained launcher. Inno Setup 6 is required for `scripts/Build-Installer.ps1 -Version 0.2.0`.

The website is plain HTML, CSS, and JavaScript in `website/dist` and can be served by any static web host.

## Privacy

Discord sign-in uses the FairPlay backend. The launcher stores its short-lived session using Windows encryption for the current user. Reopening checks live membership and verification; stored data does not grant access by itself. Sign out removes the saved credential. Game backups and recovery records stay on your PC.

Never share your `CommunityFrontier` user-data folder or game recovery files.

## Notices

RDO FairPlay is an independent community project, not affiliated with or endorsed by Rockstar Games or Take-Two Interactive. Red Dead Online and related marks belong to their respective owners. Required third-party notices are in `docs/THIRD_PARTY_LICENSE.txt`.
