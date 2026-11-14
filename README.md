# WatchPointer for Windows

Use your Apple Watch as an air mouse for a **Windows** PC. Point the watch at the screen to move the cursor, tap to
click, hold to drag, turn the Digital Crown to scroll — plus presentation, media and custom shortcut profiles.

A Windows receiver and companion watch app modelled on
[WatchPointer](https://github.com/umarz317/WatchPointer) by Umar Afzal (macOS, MIT). The Bluetooth protocol is kept
byte-for-byte compatible, so **the original watch app works with this Windows receiver**, and this project's watch app
adds gestures, remote buttons and latency measurement on top (protocol v2).

No Wi-Fi, no servers, no accounts: the watch talks straight to the PC over Bluetooth Low Energy.

## How it works

```
Apple Watch (BLE central)                               Windows PC (BLE peripheral / GATT server)
CoreMotion 100 Hz ─► aim (gravity ↑↓, gyro ←→) ─BLE─►  decode ─► 1€ filter ─► laser projection ─► 240 Hz SendInput
pad / buttons / Crown ─► click, drag, scroll, gestures ─►  profile ─► mouse, keys, media keys, macros
```

watchOS apps can only be a BLE **central**, so the PC must host the GATT server. **Your PC's Bluetooth adapter must
support the LE peripheral role.** Check it first:

```
watchpointer-cli ble-check
```

## Status

The Windows receiver is complete and tested (automated tests, simulator, measured performance). The watch app
source is complete and builds green on GitHub's macOS runners. End-to-end with a real watch has **not** been verified
yet — see the honest [feature checklist](docs/CHECKLIST.md).

Measured on the development PC (simulated watch, real cursor loop): motion prediction cuts the cursor-to-hand error
from ~45 px to ~8 px and the lag from ~42 ms to ≈0 ms at a typical Bluetooth interval
([details](docs/ACCURACY.md)); 240 Hz cursor updates, timer wake-up lateness
p95 ≈ 0.6 ms, PC-side pipeline latency p50 ≈ 2 ms, engine CPU ≈ 0.15 % of one core. Bluetooth latency depends on the
watch and adapter; the app measures it (round trip) instead of guessing.

## Quick start

1. **Windows**: install the [.NET 10 SDK](https://dotnet.microsoft.com/download), then
   `./build.ps1` and run `artifacts/WatchPointer-win-x64/WatchPointer.exe` — or download the
   *WatchPointer-win-x64* artifact from the *Windows receiver* GitHub Actions run.
   Details: [docs/WINDOWS_INSTALL.md](docs/WINDOWS_INSTALL.md).
2. **Watch**: building is free on GitHub Actions; installing needs signing (a Mac with a free Apple ID, or a paid
   developer account with TestFlight). Details and the full "what needs what" table:
   [docs/WATCH_INSTALL.md](docs/WATCH_INSTALL.md).
3. Open WatchPointer on the watch, tap **Pair** next to your PC, point at the middle of the screen, tap **Center**.

No watch yet? **Diagnostics → Simulator** drives the real engine with a virtual watch.

## Features

| | |
|---|---|
| Pointing | Absolute "laser" mode (default, original behaviour) or relative air-mouse mode; independent horizontal/vertical speed; angle limits; guided calibration; recenter from watch, dashboard or hotkey; optional edge re-anchoring against drift |
| Smoothing | 1€ filter driven by the watch's own timestamps; 240 Hz glide between samples on a dedicated high-priority thread |
| Clicks | Tap = click placed where you pointed at touch-down; two taps = double click; hold = drag; right-click button; system Double Tap (Series 9 / Ultra 2+) |
| Scrolling | Digital Crown: smooth or notched, inverted, speed; per profile: horizontal scroll, zoom, volume, slide steps |
| Profiles | Desktop (window snapping, Task View, virtual desktops, app switching), Presentation (slides, start/end, black screen, laser dot), Media (play/pause, tracks, volume, mute), Custom (any shortcut or macro) |
| Displays | Multiple monitors, negative coordinates, mixed DPI, choose primary / all / one display as the pointing area |
| Dashboard | Connection, Bluetooth state, battery, motion rate, measured latency, mode, profile, recenter, pause, laser |
| Diagnostics | Packet stats and invalid-packet reasons, BLE arrival jitter, round trip, pipeline latency, timer accuracy, CPU, live log, copyable report, simulator |
| Reliability | Adapter capability check, automatic restart with backoff, radio on/off handling, connection timeout, drag released on silence |

## Repository layout

```
WatchApp/        watchOS app (SwiftUI) + WatchCore Swift package (tested on Linux/macOS CI)
WindowsApp/      WatchPointer.Core (logic) · .Platform (BLE, SendInput) · .App (WPF) · .Tool (CLI)
Shared/Protocol/ PROTOCOL.md + test-vectors.json shared by both test suites
Tests/           xUnit tests
docs/            analysis, architecture, install guides, Bluetooth guide, troubleshooting, checklist
```

## Documentation

- [Analysis of the original project & technical risks](docs/ANALYSIS.md)
- [Architecture](docs/ARCHITECTURE.md)
- [BLE protocol](Shared/Protocol/PROTOCOL.md)
- [Windows installation](docs/WINDOWS_INSTALL.md)
- [Apple Watch installation (free vs paid, what needs a Mac)](docs/WATCH_INSTALL.md)
- [Bluetooth compatibility](docs/BLUETOOTH.md)
- [Troubleshooting](docs/TROUBLESHOOTING.md)
- [Pointer accuracy & responsiveness](docs/ACCURACY.md)
- [Feature checklist](docs/CHECKLIST.md)

## Security note

Like the original, the receiver accepts any nearby device that speaks the protocol while it runs. Use *Only accept
input from the trusted watch* (a convenience filter, not authentication) and stop the receiver when not in use.

## Downloads and verification

Releases (GitHub › Releases) contain `WatchPointer.exe` as a single self-contained file, the unsigned watch `.ipa`,
SHA-256 checksums and build-provenance attestations. See [docs/RELEASING.md](docs/RELEASING.md) for how to verify a
download and how releases are made.

## License

Copyright © 2026 sandareddysaichandan. MIT — see [LICENSE](LICENSE). Includes work derived from WatchPointer by Umar Afzal (MIT); see
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
