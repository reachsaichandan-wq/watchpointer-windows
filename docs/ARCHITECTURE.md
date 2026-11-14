# Architecture

```
WatchPointer/
├── WatchApp/                        watchOS (Swift, SwiftUI, CoreMotion, CoreBluetooth)
│   ├── project.yml                  XcodeGen spec → WatchPointer.xcodeproj (generated, not committed)
│   ├── WatchCore/                   Swift package, Foundation only, tested on Linux + macOS
│   │   ├── Sources/WatchCore/       Protocol.swift · AimTracker.swift · Gestures.swift
│   │   └── Tests/WatchCoreTests/    uses Shared/Protocol/test-vectors.json
│   └── WatchPointer/                the app
│       ├── Bluetooth/PCLink.swift   central: scan, pair, connect, write, ping/pong, PC info
│       ├── Motion/MotionService.swift   CMMotionManager → AimTracker, wrist flick
│       ├── Gestures/ClickPad.swift  pad → PadGestureClassifier → packets
│       ├── Settings/ · Views/       SwiftUI screens (remote, pairing, remote buttons, settings, calibrate)
│       └── WatchPointerApp.swift    Session: glue + battery-aware motion lifecycle
├── WindowsApp/
│   ├── WatchPointer.Core/           net10.0, no Windows APIs → unit-testable
│   │   ├── Protocol/                packets, codec, validation, info JSON
│   │   ├── Motion/                  OneEuroFilter, WatchClock (unwrap + sync)
│   │   ├── Pointer/                 AbsoluteProjector, RelativeMover, DisplayLayout, settings, SendInput normalisation
│   │   ├── Session/                 PointerEngine (the receiver logic), ActionRunner (macros), engine types
│   │   ├── Bluetooth/               IPeripheralTransport, AdapterCapabilities, ReceiverSupervisor (reconnect)
│   │   ├── Profiles/ Input/         profiles, gesture→action mapping, key names, macro parser
│   │   ├── Settings/                AppSettings + JSON store
│   │   ├── Calibration/ Diagnostics/ Simulation/
│   ├── WatchPointer.Platform/       net10.0-windows: GattPeripheralTransport (WinRT), Win32InputSink (SendInput),
│   │                                Win32DisplayProvider, CursorLoop (high-resolution waitable timer)
│   ├── WatchPointer.App/            WPF UI (Fluent theme, MVVM, DI), tray, hotkeys, laser overlay
│   └── WatchPointer.Tool/           `watchpointer` CLI: ble-check, listen, simulate, bench, displays
├── Shared/Protocol/                 PROTOCOL.md + test-vectors.json (single source of truth for both languages)
├── Tests/WatchPointer.Core.Tests/   xUnit
└── .github/workflows/               windows.yml · watchos.yml · watchos-testflight.yml (optional, paid)
```

## Data flow (Windows)

```
 BLE radio ──► GattPeripheralTransport.WriteRequested (thread pool, timestamp taken first)
                  │ bytes, arrival
                  ▼
             ReceiverSupervisor ──► PointerEngine.HandleRaw ──► PacketCodec.TryDecode ──(invalid → counted)
                  │ connect/disconnect,                 │
                  │ retries, pings                      ├─ aim: WatchClock → projection/relative → 1€ filter → target
                  │                                     ├─ click/drag: history rewind → IInputSink
                  │                                     ├─ scroll/gesture: profile → IInputSink / ActionRunner
                  ▼                                     ▼
             (state for UI)              CursorLoop thread (240 Hz, highest priority)
                                           PointerEngine.Tick → glide → clamp to monitors → SendInput
UI thread (WPF): polls PointerEngine.Snapshot() 4×/s; never on the input path.
```

One short lock (`PointerEngine._gate`) serialises the BLE thread and the cursor thread. Notices (connected, lost,
paused…) are raised after the lock is released. Key macros run on their own worker so a `wait 500` never delays the
pointer.

## Key decisions

| Decision | Why |
|---|---|
| Keep protocol v1 byte-for-byte; extend additively (v2) | The original watch app works unchanged; the new watch app works with the original Mac receiver. |
| Core has no Windows dependencies | 176 unit/integration tests run anywhere; the simulator and benchmark drive the real engine. |
| WPF + Fluent theme (`ThemeMode`) instead of WinUI 3 | No Windows App SDK runtime to install, unpackaged `.exe`, dark/light/system themes built in, simpler CI. |
| WinRT `GattServiceProvider` from an unpackaged desktop app | Works without MSIX packaging/capabilities for desktop apps. |
| `SendInput` absolute + `VIRTUALDESK` with exact normalisation | Real injected input (drag works everywhere), exact physical pixels on mixed-DPI multi-monitor setups (tested for every pixel of several layouts). |
| High-resolution waitable timer for the cursor loop | Measured 240 Hz with p95 wake-up lateness ≈ 0.6 ms on the dev PC; idles at 20 Hz when no watch is steering. |
| Filters use watch timestamps | BLE delivers samples in bursts; arrival times would make the 1€ filter shake and lag (same as original). |
| Profiles map inputs to actions on the PC | The watch stays simple and needn't be re-installed to change mappings. |
| XcodeGen instead of a committed `.pbxproj` | The project file can't be produced or checked on Windows; a small YAML spec is reviewable and CI generates the project. |

## Dependency injection & logging

`App.xaml.cs` builds a `ServiceCollection`: settings store, `ReceiverHost` (composition of transport, supervisor,
engine, loop, action runner, simulator), view models. Logging is `Microsoft.Extensions.Logging` with structured
message templates, written to an in-memory ring (Diagnostics page) and to
`%LOCALAPPDATA%\WatchPointer\logs\watchpointer-YYYYMMDD.log` (14 days kept).

## Testing strategy

- **Protocol**: shared vectors (C# + Swift), fuzzing with random bytes, round trips.
- **Math**: projection (tangent law, inverse, clamping, inversion), relative mover (dead zone, sub-pixel, wrap,
  acceleration), display layout (gaps, negative coordinates), SendInput normalisation (every pixel), calibration.
- **Engine** with a manual clock: centre, edges, smoothing under tremor, touch-down click rewind, double click, drag,
  drop on silence, idle/lost, fresh jump, pause, recenter, edge re-anchoring, multi-monitor, relative mode, scroll
  quantisation, invalid/reordered packets, untrusted watch, latency probe, pipeline latency.
- **Gestures/profiles**: per-profile mapping (desktop, presentation, media, custom), hold vs press actions.
- **Reconnection**: fake transport — unsupported adapter, no adapter, backoff, permanent failure, aborted
  advertising, radio off/on, connect/disconnect/reconnect, silent-connection timeout, v1 vs v2 notifications.
- **End-to-end simulation**: virtual watch with tremor and bursty BLE delivery → smoothness, tracking error,
  steadiness.
- **Watch**: AimTracker (gravity elevation, gyro heading, clamp, wrap, drift trim), pad classifier (tap, hold, four
  swipes, cancel, slow drag), wrist flick, scroll accumulator.
