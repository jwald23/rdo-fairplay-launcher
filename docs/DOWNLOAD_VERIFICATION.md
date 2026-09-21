# Verify a FairPlay download

Download only from [official GitHub Releases](https://github.com/jwald23/rdo-fairplay-launcher/releases). Each release includes public source, build and test results, and SHA256 checksums. Releases with a **signed GitHub build provenance** link also support the verification below; older releases may not have attestations.

## Verify the build origin

Install the [GitHub CLI](https://cli.github.com/) and, in PowerShell in your download folder, run:

```powershell
gh attestation verify .\RDOFairPlay-Setup-Windows-x64.exe --repo jwald23/rdo-fairplay-launcher --signer-workflow jwald23/rdo-fairplay-launcher/.github/workflows/launcher-release.yml
```

For the portable download, substitute `RDOFairPlay-Windows-x64.zip`. The CLI may ask you to authenticate with GitHub. Verification checks the file digest and signed build record against this repository and its release workflow. Stop if verification fails; do not run the file until the failure is understood.

The release page links to the attestation, exact source commit, and workflow run. The workflow builds the public source on GitHub, runs launcher tests and installation/uninstallation checks, and verifies attestations before publishing.

## Check the checksum

Download `SHA256SUMS.txt` from the **same release** as your installer or ZIP. Run:

```powershell
Get-FileHash .\RDOFairPlay-Setup-Windows-x64.exe -Algorithm SHA256
```

Compare the entire hash with the matching filename in `SHA256SUMS.txt`; letter case does not matter. A matching checksum detects changed or damaged downloads. A checksum alone does not establish trust in its publisher.

## Browser and Windows warnings

The current downloads are unsigned and may require confirmation in your browser and Windows. Warnings vary by browser, Windows version, and device policy; they are not always shown. The installer does not require administrator access.

1. Download from the official GitHub release above. Verify the checksum and build provenance before choosing to run it.
2. If Microsoft Edge says the file is **not commonly downloaded**, open its Downloads panel and the file's menu. If you have verified the download and trust it, select **Keep**, then **Show more → Keep anyway** if offered. This is for a reputation warning, not a malware detection. Other browsers use different wording.
3. Scan the saved file with your antivirus. With Microsoft Defender, right-click the file and choose **Scan with Microsoft Defender** (on Windows 11, this may be under **Show more options**).
4. If Windows says **Windows protected your PC** because the app is unrecognized, **More info → Run anyway** may be available. Choose it only after verification and only if you trust this release. If your device policy or Smart App Control blocks it without that option, contact Support or your administrator; do not turn off those protections.
5. If your antivirus identifies a threat or quarantines the file, stop. Keep it quarantined and send Support the release version, antivirus product, and exact detection name. We can investigate and submit a suspected false positive to the vendor. Do not disable antivirus, add exclusions, or restore a detected file just to run FairPlay.

An uncommon-download warning is not itself a malware verdict. Equally, an antivirus detection must not automatically be dismissed as a false positive. Checksums and provenance do not guarantee that code is safe.

See [Microsoft's Edge download guidance](https://learn.microsoft.com/en-us/deployedge/microsoft-edge-security-smartscreen) and [Microsoft's file-analysis submission portal](https://www.microsoft.com/en-us/wdsi/filesubmission).

## Limits of verification

No badge, antivirus result, or signature proves that software cannot be malicious. Public source allows inspection; provenance identifies the build origin; hashes verify file integrity. These do not independently audit the code or guarantee its behavior.

The installer is currently **not Windows Authenticode signed**. GitHub provenance does not remove SmartScreen warnings or establish a Windows publisher identity. Keep Windows security protections enabled. You can scan the downloaded file using your antivirus before deciding whether to run it. A clean scan is additional evidence, not a guarantee.

For details, see [GitHub's artifact attestation documentation](https://docs.github.com/en/actions/concepts/security/artifact-attestations).
