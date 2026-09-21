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

## Understand what these checks mean

No badge, antivirus result, or signature proves that software cannot be malicious. Public source allows inspection; provenance identifies the build origin; hashes verify file integrity. These do not independently audit the code or guarantee its behavior.

The installer is currently **not Windows Authenticode signed**. GitHub provenance does not remove SmartScreen warnings or establish a Windows publisher identity. Keep Windows security protections enabled. You can scan the downloaded file using your antivirus before deciding whether to run it. A clean scan is additional evidence, not a guarantee.

For details, see [GitHub's artifact attestation documentation](https://docs.github.com/en/actions/concepts/security/artifact-attestations).
