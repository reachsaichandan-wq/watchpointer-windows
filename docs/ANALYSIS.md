# Analysis of the original WatchPointer

Source analysed: [umarz317/WatchPointer](https://github.com/umarz317/WatchPointer), commit `504137a` (7 Nov 2026),
MIT License. ~600 lines of Swift: `Watch/` (watchOS app, 2 files) and `Mac/` (Swift package receiver, 7 files),
plus a browser game.

## Watch side (`WatchPointerApp.swift`, `ContentView.swift`)

| Area | Implementation |
|---|---|
| Motion | `CMMotionManager.startDeviceMotionUpdates` at **100 Hz** on the main queue. |
| Elevation (up/down) | From **gravity** only: `up = −g/|g|`, `elevation = asin(up.y)` (device +y = 12 o'clock edge). Never drifts. |
| Heading (left/right) | **Integrated gyro** around the vertical: `heading += (rotationRate · up) · dt`, `dt ≤ 50 ms`. No dead zone (comment: dropping slow turns made an older version slide out of line). Drifts slowly; "Center" resets it. |
| Center | `heading = 0`, `elevationZero = elevation`, sends kind 4. Also done on every connect. |
| Packets | `[kind, a:i16, b:i16 (, ms:u16)]`, aim in **hundredths of a degree**, `left` positive and `up` positive, plus the watch's ms clock. |
| Aim sending | *write without response*, skipped when `canSendWriteWithoutResponse` is false (newest sample wins). |
| Clicks / drag | Pad `DragGesture(minimumDistance: 0)`: tap → leftClick stamped with **touch-down** time; hold ≥ **0.35 s** → drag grab (stamped touch-down), release → drop (release − 0.05 s). Right-click button stamps `release − 0.12 s`. |
| Scroll | `digitalCrownRotation`, 100 "pixels" per Crown unit, fractional remainder kept; flushed on the fast lane, retried each motion sample. |
| Pause | Toggles; aim keeps tracking but isn't sent. |
| Info | JSON `{model, os, battery, charging, name}` on connect and every 60 s (with response). |
| BLE role | **Central** (`CBCentralManager`). Scans for the service UUID; pairing = remembering the peripheral UUID in `UserDefaults`, then `retrievePeripherals` + scan filtered to that id. Subscribes to the output characteristic so the Mac sees connect/disconnect. |
| UI | One big pad, three buttons (right click, center, pause), unpair via toolbar, haptics, watch-only app (`WKWatchOnly`), watchOS 11. |

## Mac side (`Receiver.swift`, `Displays.swift`)

| Area | Implementation |
|---|---|
| BLE role | **Peripheral** (`CBPeripheralManager`): service `6B1E0001…`, input `…0002` (write + write w/o response), output `…0003` (notify, never sent). Advertises the service UUID + the Mac's name. Requests low connection latency on first write. |
| Projection | Laser on a wall: `h = clamp(−left, ±80°)`, `v = clamp(up, ±80°)`, `D = (W/2)/tan(25°/speed)`, `x = cx + tan(h)·D`, `y = cy − tan(v)/cos(h)·D`, over the **main display**. |
| Filter | 1€ filter on x and y in pixels, `minCutoff 0.8 Hz` ("steady"), `beta 0.01` ("quick"), `dCutoff 1 Hz`, using **watch timestamps** for dt. dt outside (0, 0.2) s → unfiltered. |
| Clock | Unwraps the 16-bit ms; `clockOffset = min(now − watchClock)`. |
| Cursor loop | Dedicated `.userInteractive` queue, **240 Hz** `DispatchSourceTimer`, exponential glide toward the target (τ ≈ 5 ms), posts `CGEvent` mouse moves / drags. |
| Gaps | > 0.3 s without samples → stop moving, drop any drag; > 1.5 s → "lost". First sample after a gap jumps (filters reset). |
| Clicks | Keeps ~1 s of cursor history; a click goes where the cursor was at the stamped touch-down time. |
| Multi-monitor | Projection relative to the main display; result clamped to the nearest display (gaps skipped). |
| Scroll | `CGEvent` pixel scroll. |
| Settings | CLI args: speed, `steady=`, `quick=`. |
| Security | No authentication; any device speaking the protocol can move the cursor (stated in the README). |

## Most important technical risks for a Windows port

1. **BLE role (critical).** watchOS can only be a central, so Windows must be the GATT server and advertise. That
   needs `GattServiceProvider` and an adapter/driver reporting `BluetoothAdapter.IsPeripheralRoleSupported`. Many
   older or cheap adapters don't. → The receiver checks this first and reports it; `watchpointer-cli ble-check` tells
   you before you install anything. Verified on the development PC: MediaTek adapter reports peripheral support.
2. **No Mac / Xcode.** Building is possible on free GitHub macOS runners; **installing** on a watch requires code
   signing, which free Apple IDs can only do through Xcode on a Mac (7-day profiles), or a paid membership with
   TestFlight. See `docs/WATCH_INSTALL.md`.
3. **Connection interval.** The watch (central) decides it; Windows can only request. The original asks for low
   latency on macOS; on Windows the equivalent API exists only on Windows 11 22H2+, best effort, result logged.
4. **Advertised name.** `GattServiceProvider` doesn't let an app set the local name, so a never-connected PC may
   appear as a generic name on the watch. → v2 info characteristic gives the real name after the first connect.
5. **Event ordering.** `WriteRequested` handlers are async; reordered aim samples are detected by timestamp and
   dropped rather than dragging the cursor back.
6. **Input injection limits.** `SendInput` can't reach elevated windows (UIPI) or the secure desktop.
7. **DPI.** Coordinates must be physical pixels across mixed-scale monitors → per-monitor-v2 awareness and
   normalised absolute `SendInput` coordinates over the virtual desktop.

## What was kept, what changed

Kept exactly: UUIDs, packet formats, motion model, projection, 1€ parameters, 240 Hz glide, gap/lost timings,
touch-down click placement, hold delay. Replaced: CoreBluetooth peripheral → WinRT `GattServiceProvider`;
`CGEvent` → `SendInput`; `CGDisplay` → `EnumDisplayMonitors`; notch UI → WPF dashboard. Added (v2, additive):
gestures, remote buttons, ping/pong latency, PC state, PC info characteristic, horizontal scroll field.
Fixed: a fresh jump with no further motion was never posted (the original compares against the previous frame);
cursor history now keeps the last position from before the 1 s window so a click after holding perfectly still
still rewinds correctly; minimum clock offset can relax so clock drift can't freeze it.
