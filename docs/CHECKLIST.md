# Feature completion checklist

Legend: ✅ implemented **and** verified here (tests / run on the dev PC) · 🟡 implemented, verified only partially
(see note) · ⏳ implemented, **not yet verified** (needs hardware or CI the dev environment didn't have) · ❌ not
implemented (with reason).

Dev environment: Windows 11 26200, .NET SDK 10.0.404, MediaTek Bluetooth adapter (radio was **off** during
development), no Mac, no Apple Watch.

## Analysis & protocol

| Feature | Status | Evidence |
|---|---|---|
| Original repo analysed (BLE roles, UUIDs, packets, math, filter, gestures, UI) | ✅ | [ANALYSIS.md](ANALYSIS.md) |
| Protocol v1 compatibility (UUIDs + all 8 packet kinds) | ✅ | shared vectors, C# tests |
| Protocol v2 extensions (gestures, pong, ping/state, PC info, horizontal scroll) | ✅ | C# tests + Swift tests in CI, same shared vectors |
| Packet validation & invalid-packet accounting | ✅ | 8 invalid vectors + 20 000-packet fuzz test |
| Protocol documented & versioned | ✅ | [PROTOCOL.md](../Shared/Protocol/PROTOCOL.md) |

## Windows receiver

| Feature | Status | Evidence / note |
|---|---|---|
| Adapter capability detection (LE, peripheral role, radio) with clear messages | ✅ | `ble-check` on dev PC: peripheral role **supported**, radio off reported |
| GATT server + advertising (`GattServiceProvider`) | ⏳ | Code complete; not run end-to-end because Bluetooth was off. Run `watchpointer-cli ble-check` with Bluetooth on. |
| Watch connection, writes, subscribe/unsubscribe | ⏳ | Needs a watch or a BLE phone app (nRF Connect) |
| Reconnection, backoff, radio off/on, silent-connection timeout | ✅ | 11 supervisor tests (fake transport) |
| Low-latency connection request (Win 11 22H2+) | ⏳ | Best effort; result logged |
| Absolute (laser) mode, tan projection, angle limits, independent H/V speed | ✅ | projector + engine tests |
| Relative (air mouse) mode, speed, acceleration, dead zone, sub-pixel | ✅ | mover + engine tests |
| 1€ filter with watch timestamps, clock sync with drift relaxation | ✅ | filter + clock tests |
| 240 Hz cursor loop, idle at 20 Hz | ✅ | measured 240–241 Hz, wake lateness p95 ≈ 0.6 ms (`simulate`) |
| Motion prediction (adaptive to measured RTT, deceleration-aware), presets Smooth / Balanced / Fast | ✅ simulated | `watchpointer-cli track`: mean error 44.9 → 8.2 px, lag 42 → ≈0 ms at a 30 ms BLE interval; [ACCURACY.md](ACCURACY.md). Not yet felt with a real watch. |
| Recenter (watch and PC side), edge re-anchoring (drift correction) | ✅ | engine tests |
| Click / right / middle / double click at touch-down position | ✅ | engine tests (rewind with and without stamps) |
| Drag (hold), drop on release / silence / disconnect | ✅ | engine tests |
| Crown scroll: smooth / notched, inverted, horizontal, zoom, volume, slides | ✅ | engine + mapping tests |
| Multi-monitor: primary / all / specific display, gap skipping, negative coords, mixed DPI | ✅ math · 🟡 live | tests; dev PC has one 1920×1080 @125 % monitor |
| SendInput absolute coordinates exact per pixel | ✅ | every pixel of 4 layouts round-trips |
| Profiles: Desktop, Presentation, Media, Custom; add/duplicate/rename/delete/reset | ✅ logic · 🟡 UI | mapping tests; UI rendered, not click-tested by hand |
| Gesture → action mapping, key chords, macros (wait, type) | ✅ | parser + mapping tests |
| Media keys, window snapping, Task View, virtual desktops, slides | ✅ (key output) | recorded key sequences in tests; actual OS reaction not automated |
| Laser pointer overlay (click-through, no focus steal) | 🟡 | builds; shown by profile/hotkey; not screenshot-verified |
| Global hotkeys | 🟡 | registered at startup; conflicts reported |
| Trusted-watch filter | ✅ | engine test |
| Encrypted (bonded) link option | ⏳ | experimental; depends on adapter/watchOS behaviour |
| Simulator (preview & live), CLI `simulate`, `bench` | ✅ | ran on dev PC |
| Diagnostics: sample rate, arrival interval, link delay, RTT, pipeline, tick cost, CPU, invalid packets, log, report | ✅ (values) | pipeline p50 ≈ 2 ms, engine ≈ 0.15 % of one core measured; RTT needs a v2 watch |
| Dashboard + 9 pages, dark/light/system theme, tray | ✅ | app launched; every page rendered (`--render-pages`) |
| Structured logging to file + UI | ✅ | log file written on dev PC |

## Apple Watch app

| Feature | Status | Note |
|---|---|---|
| 100 Hz CoreMotion, gravity elevation, gyro heading (original model) | ⏳ | Pure math tested in `WatchCore` (CI); app compiles only in CI |
| Pairing list (closest first), remembered PC, forget | ⏳ | |
| Pad: tap, hold-drag, swipes (4 directions) | ⏳ | classifier unit-tested in CI |
| Right click, Center, Pause buttons, Crown scroll with speed/invert | ⏳ | |
| Remote buttons A–D, system Double Tap (`handGestureShortcut`) | ⏳ | Double Tap only on supported watches |
| Wrist flick (opt-in), drift trim (opt-in) | ⏳ | detectors unit-tested |
| Connection status, PC profile name, battery | ⏳ | |
| Battery-aware motion (connected + foreground only; 25 Hz when paused) | ⏳ | |
| Ping/pong latency, PC info name | ⏳ | |
| Builds on GitHub Actions (simulator + device, unsigned) | ✅ | green on github.com/reachsaichandan-wq/watchpointer-windows (Xcode 16.4); WatchCore tests pass on Linux and macOS |
| TestFlight workflow (paid account) | ⏳ | unverified by design (no paid account) |

## Not implemented (by decision)

| Feature | Reason |
|---|---|
| Wrist *shake* → Escape | Can't be told apart from pointing reliably; Escape is mapped to swipe down / wrist flick / remote buttons instead. |
| "Two quick taps → play/pause" | Two quick taps are needed for double-click. Play/pause is on the Media profile's tap, Double Tap and button B. |
| Wi-Fi / network fallback when the adapter can't be a peripheral | watchOS restricts low-level networking; see [BLUETOOTH.md](BLUETOOTH.md). |
| macOS receiver | Out of scope (Windows is the target). The original covers macOS. |
| MSIX installer / code signing | Costs money (certificate); portable folder instead. |
