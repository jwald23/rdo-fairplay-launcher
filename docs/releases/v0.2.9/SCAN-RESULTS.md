## Antivirus scan results — September 24, 2026

**These downloads have unresolved antivirus detections. They are not being described as clean or independently audited.** Check the live reports, dates and SHA256 values before deciding whether to run them.

| File | VirusTotal result | Detection | Analysis date (UTC) |
| --- | --- | --- | --- |
| Windows installer | [1/48 reporting engines](https://www.virustotal.com/gui/file/bc265e114ec6e78a45cee39480aa97a77179a74f7e52fa54a65b1c2c42237e1f/detection) | DeepInstinct: `MALICIOUS` | 2026-09-24 12:41:32 |
| Portable ZIP | [1/66 reporting engines](https://www.virustotal.com/gui/file/81ea498549aa5455073a58844990665127d11b4ae5aee4f53ff1a5f6d7f5955b/detection) | VBA32: `Trojan.Win32.Wiper` | 2026-09-24 12:42:21 |

Installer: 47 Undetected, one detection, 22 timeouts (including one confirmed timeout), five unsupported file types. ZIP: 65 Undetected, one detection, two timeouts, seven unsupported file types. Timeouts and unsupported results are not clean verdicts. These are observed snapshots, not permanent scores.

**Archive limitation:** VirusTotal warned that this ZIP exceeds its 3 MB/single-contained-file archive extraction limit. Its result must not be presented as complete coverage of every contained executable or library.

### Additional evidence

- A publisher-performed Microsoft Defender custom scan found no threats in the exact public installer and ZIP, and a separate scan found no threats across the 405 files extracted from that ZIP. Both returned exit code 0. This is a local scan, not an independent audit or Microsoft certification.
- Defender engine: `1.1.26080.3`; definitions: `1.459.362.0`, updated 2026-09-23 14:00:14 UTC. Download scan completed 2026-09-24 12:41:05 UTC; extracted-package scan completed 12:42:35 UTC. See the [Defender scan record](DEFENDER-SCAN.json).
- The release's NuGet advisory check reported zero vulnerable package entries for the launcher's direct and transitive NuGet packages. This does not cover the bundled .NET runtime, installer tooling, unknown flaws or application behavior.
- Build, launcher tests, installer lifecycle tests and GitHub provenance verification passed. The installer remains Windows Authenticode **unsigned**.

### Exact files

- Installer SHA256: `bc265e114ec6e78a45cee39480aa97a77179a74f7e52fa54a65b1c2c42237e1f`
- ZIP SHA256: `81ea498549aa5455073a58844990665127d11b4ae5aee4f53ff1a5f6d7f5955b`

Neither detection has been confirmed as a false positive. Different scanner results do not cancel one another out. Keep antivirus enabled and do not bypass a malware block. Vendor review and an independent code/behavior review are appropriate next steps; no vendor review outcome is claimed here. These results cover v0.2.9 only.
