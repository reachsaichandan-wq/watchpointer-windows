# WatchPointer BLE protocol (v2)

Version 2 is a strict superset of the protocol used by the original
[WatchPointer](https://github.com/umarz317/WatchPointer) (called v1 here). The UUIDs and every v1 packet are
unchanged, so:

- the **original watch app** works with the **Windows receiver** (v1 features: aim, click, drag, right click,
  scroll, center, pause, info);
- **this project's watch app** works with the **original macOS receiver** (the Mac ignores v2 packet kinds and the
  extra characteristic).

The machine-checked source of truth is [`test-vectors.json`](test-vectors.json): both the C# tests and the Swift tests
decode/encode every vector.

## Roles

| Device | BLE role | Why |
|---|---|---|
| PC (Windows) | **Peripheral / GATT server**, advertises the service | watchOS's Core Bluetooth has no `CBPeripheralManager`; a watch app can only be a central |
| Apple Watch | **Central / GATT client**, scans, connects, writes | |

Consequence: the PC's Bluetooth adapter must support the LE peripheral role (see `docs/BLUETOOTH.md`).

## GATT layout

| UUID | Name | Properties | Direction | Notes |
|---|---|---|---|---|
| `6B1E0001-9F4C-4C2A-9B8E-3D2A1C0F5E01` | Service | primary | | advertised |
| `6B1E0002-9F4C-4C2A-9B8E-3D2A1C0F5E01` | Input | write, write without response | watch → PC | every packet below |
| `6B1E0003-9F4C-4C2A-9B8E-3D2A1C0F5E01` | Output | notify | PC → watch | subscribe = connected, unsubscribe = disconnected. v1: never sent. v2: ping/state, **only** to watches that reported `proto ≥ 2` |
| `6B1E0004-9F4C-4C2A-9B8E-3D2A1C0F5E01` | Info (v2) | read (static) | PC → watch | JSON `{"name","os","proto","app"}` |

Write types: `aim` and `scroll` use *write without response* (if the link is busy the next sample replaces it);
everything else uses *write with response* so clicks are never dropped. A pong uses without-response when possible.

## Packets (watch → PC, input characteristic)

All integers little-endian. Fixed packets are `[kind:u8][a:i16][b:i16]` (5 bytes), optionally followed by
`[ms:u16]` (7 bytes): the watch's clock (`ProcessInfo.systemUptime`, the same clock as CoreMotion timestamps) in
milliseconds, truncated to 16 bits (wraps every 65.536 s). Receivers must accept both lengths and should ignore
trailing bytes up to 20 total (forward compatibility).

| kind | name | a | b | ms | since |
|---|---|---|---|---|---|
| 0 | aim | heading, centidegrees, **left positive**, wrapped to ±180° | elevation since Center, centidegrees, **up positive** | when measured | v1 |
| 1 | leftClick | 0 | 0 | touch-down time | v1 |
| 2 | rightClick | 0 | 0 | ≈ touch-down (release − 0.12 s) | v1 |
| 3 | scroll | vertical "pixels", positive = up | v1: 0. v2: horizontal pixels, positive = right | — | v1 / v2 |
| 4 | center | 0 | 0 | — | v1 |
| 5 | pause | 1 paused / 0 resumed | 0 | — | v1 |
| 6 | info | `[6] + UTF-8 JSON` (not fixed size, ≤ 512 bytes) | | | v1 |
| 7 | drag | 1 grab (mouse down) / 0 drop (mouse up) | 0 | touch-down (grab) / just before lift (drop) | v1 |
| 8 | gesture | gesture id (below) | 0 | when it happened | **v2** |
| 9 | pong | ping sequence number (u16 reinterpreted) | 0 | watch time | **v2** |

Info JSON (v1 fields + v2 additions):

```json
{"model":"Watch7,5","os":"11.0","battery":80,"charging":false,"name":"Sam's Watch","proto":2,"app":"1.0.0"}
```

`battery` is a percentage or −1 when unknown. Missing `proto` means 1. Sent on connect and every 60 s.

Gesture ids (kind 8): 1 swipe left, 2 swipe right, 3 swipe up, 4 swipe down, 5 system Double Tap,
6 wrist flick, 16–19 remote buttons A–D. Unknown ids are rejected as out of range.

### Validation rules (receiver)

| Error | Rule |
|---|---|
| Empty | 0 bytes |
| TooShort | non-info packet < 5 bytes |
| TooLong | non-info > 20 bytes, info > 512 bytes |
| UnknownKind | first byte > 9 |
| BadInfo | info JSON doesn't parse |
| OutOfRange | aim \|a\| or \|b\| > 18000; unknown gesture id |

Invalid packets are counted per reason (Diagnostics page) and dropped. Aim samples whose timestamp is older than the
newest applied one (signed 16-bit difference) are dropped as reordered. Info fields are truncated and battery is
clamped.

## Notifications (PC → watch, output characteristic, v2 only)

| First byte | Message | Layout |
|---|---|---|
| 1 | ping | `[1][seq:u16]` — the watch answers with a pong packet carrying `seq` |
| 2 | state | `[2][flags:u8][UTF-8 profile name, ≤ 17 bytes]` — flags bit0 PC paused, bit1 laser on |

The receiver sends a ping every 2 s and the state every 5 s and on change, only while a v2 watch is connected. The
RTT is measured entirely on the PC clock (no synchronisation needed).

## Timing model (receiver)

- **Clock sync**: the 16-bit ms stamps are unwrapped into a continuous watch clock. `offset = PC time − watch time`
  is the minimum seen (the quickest-arriving sample), allowed to relax upward by 50 ppm so crystal drift can't
  freeze it.
- **Filtering** uses the watch's sample spacing as `dt` (Bluetooth delivers samples in bursts every connection
  interval, so arrival times would be wrong).
- **Clicks/drags** with a stamp are placed where the cursor was at that PC-converted time (history of the last ~1 s),
  so the jolt of tapping the watch doesn't move the click. Without a stamp: 0.1 s rewind (click), 0.45 s (grab).

## Versioning

`proto` in the info JSON is the watch's version; the PC's is in the info characteristic. New features must be
additive: new kinds, new gesture ids, new notification types, optional trailing bytes. A receiver must never send
notifications to a watch that hasn't announced support.
