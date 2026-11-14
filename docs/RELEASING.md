# Releasing and trusting the Windows build

Copyright © 2026 sandareddysaichandan. MIT License (see `LICENSE`; third-party notices in `THIRD_PARTY_NOTICES.md`).

## What a release contains

| File | What it is |
|---|---|
| `WatchPointer-<version>-win-x64.zip` | `WatchPointer.exe` (the app) and `watchpointer-cli.exe` (diagnostics). Self-contained single-file executables: no installer, no .NET runtime needed. Plus `LICENSE`, `THIRD_PARTY_NOTICES.md`, `README.md`. |
| `WatchPointer-<version>-unsigned.ipa` | iPhone app with the embedded watch app. Must be signed before installing ([WATCH_INSTALL.md](WATCH_INSTALL.md)). |
| `SHA256SUMS.txt` | SHA-256 of each file. |

Every executable carries version information (Properties › Details): product *WatchPointer for Windows*, company and
author *sandareddysaichandan*, the copyright line, the version, and the git commit it was built from (product
version `1.0.0+<commit>`; the About page shows it too).

## How to publish a release

```bash
git tag v1.0.0
git push reach v1.0.0
```

The `Release` workflow then runs the tests, publishes the executables with the tag's version, builds the `.ipa` on
macOS, writes checksums, creates build-provenance attestations and publishes the GitHub Release. To try it without
publishing, run the workflow manually (Actions › Release › Run workflow): it creates a **draft** release.

## Why users can trust a build

1. **Open source**: everything that goes into the executables is in this repository.
2. **Built in public CI from a tagged commit**, not on a personal machine; the logs are public.
3. **Checksums**: compare the download with `SHA256SUMS.txt`:
   ```powershell
   Get-FileHash .\WatchPointer-1.0.0-win-x64.zip -Algorithm SHA256
   ```
4. **Provenance attestations** (Sigstore, via GitHub, free): cryptographic proof that a file was produced by this
   repository's release workflow at a given commit:
   ```bash
   gh attestation verify WatchPointer-1.0.0-win-x64.zip --repo reachsaichandan-wq/watchpointer-windows
   ```
5. **No network, no telemetry, no admin rights**: the app only uses Bluetooth LE and local input APIs; it runs as a
   normal user (`asInvoker` manifest).

## SmartScreen and code signing

Windows SmartScreen warns about executables that are not **Authenticode-signed** by a certificate from a trusted
authority ("Windows protected your PC" / unknown publisher). Checksums and attestations prove integrity to people
who check, but SmartScreen doesn't read them. Removing the warning requires a code-signing certificate, which this
project can't create for you. Options, cheapest first:

| Option | Cost | Notes |
|---|---|---|
| [SignPath Foundation](https://signpath.org) | Free for qualifying open-source projects | Apply with the public repository; signing happens in their service from CI (they provide a GitHub Action). Their terms decide eligibility. |
| Azure Trusted Signing (Microsoft) | Monthly fee | Cloud signing from GitHub Actions; requires identity validation; check availability for individuals in your country. |
| OV / EV certificate from a CA (DigiCert, Sectigo, …) | Yearly fee | Since June 2023 the key must live in hardware or a cloud HSM, so use the CA's cloud-signing tool in CI rather than a `.pfx`. |

When you have one, replace (or fill in) the *Sign executables* step in `.github/workflows/release.yml`. Signed
builds still build SmartScreen reputation over time; EV certificates start with more.

Until then, tell users: verify the checksum (or attestation), then *More info › Run anyway*.

## Changing the author or version

- Author, company, copyright, product name: `Directory.Build.props` (Windows) and `INFOPLIST_KEY_NSHumanReadableCopyright`
  in `WatchApp/project.yml` (watch/iPhone).
- Version: set by the release tag; for local builds, `Version` in `Directory.Build.props`.
- Icon: `tools/make-icon.ps1` regenerates `WindowsApp/WatchPointer.App/Assets/WatchPointer.ico`.
