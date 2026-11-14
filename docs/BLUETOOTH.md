# Bluetooth compatibility guide

## The one requirement that matters

Apple Watch apps can only be a **BLE central**. So the PC must be the **peripheral**: publish a GATT service and
advertise it. On Windows this is `GattServiceProvider`, and it only works if the adapter's driver reports
**peripheral role support**.

Check before anything else:

```
watchpointer-cli ble-check
```

or open **Device pairing** in the app. Exit code 0 / "This PC can host WatchPointer" means it advertised
successfully. Typical outputs:

| Result | Meaning | What to do |
|---|---|---|
| `Peripheral role (required) True`, `OK: advertising…` | Ready | — |
| `Bluetooth is turned off` | Radio off | Settings › Bluetooth & devices › Bluetooth on |
| `No Bluetooth adapter was found` | No adapter / disabled | Enable it in Device Manager or add a USB adapter |
| `does not support Bluetooth Low Energy` | Bluetooth Classic only (very old adapters) | Use a Bluetooth 4.0+ LE adapter |
| `does not support the BLE peripheral (GATT server) role` | Driver doesn't expose peripheral role | Update the driver from the PC/adapter vendor; otherwise use a different adapter |
| `Creating the GATT service failed: DisabledByPolicy` | Blocked by group policy / MDM | Ask your administrator |
| `Advertising failed: Aborted` | Driver accepted the service but couldn't advertise | Update driver; turn Bluetooth off/on; another app may hold advertising slots |

## Verified hardware

| Adapter | Windows | Peripheral role | Advertising | Watch connected | Notes |
|---|---|---|---|---|---|
| MediaTek Bluetooth Adapter (built in, development PC) | 11 (26200) | ✅ reported | not yet tested (radio was off) | not yet tested | `ble-check` output, Nov 2026 |

Please add rows (via an issue or PR) with the output of `watchpointer-cli ble-check` and whether a watch connected.

General guidance (not guarantees — drivers differ even for the same chip):

- Recent **Intel** Wi-Fi/Bluetooth combo cards (AX200/AX201/AX210/AX211 and newer) and recent **MediaTek** and
  **Realtek** combo cards generally ship Windows drivers with peripheral-role support. Always confirm with
  `ble-check`.
- Cheap USB "CSR 4.0" clones and old Broadcom drivers often report no peripheral role.
- A USB adapter based on **Realtek RTL8761B/RTL8761BU** (sold under many brands) is a common low-cost choice
  people use for Windows BLE peripheral work; confirm with `ble-check` after installing its driver.
- When you add a USB adapter, disable the built-in one in Device Manager: Windows uses one radio at a time.

## If your adapter can't be a peripheral

There is no software-only fix on the PC side: Windows can't make an adapter do a role its driver doesn't expose,
and the watch can't switch roles. Technically valid alternatives:

1. **Another BLE adapter** whose driver supports the peripheral role (cheapest, ~10–20 USD).
2. **Another PC** with a capable adapter, for testing.
3. **The simulator** (Diagnostics page, or `watchpointer-cli simulate`) exercises the whole engine without Bluetooth.

Not offered, and why:

- *Wi-Fi instead of Bluetooth*: watchOS restricts low-level networking (sockets/WebSocket) to special cases such as
  audio streaming apps; plain HTTP via `URLSession` is allowed but routed through the iPhone with latency far too high
  and irregular for a pointer.
- *Relay through the iPhone*: WatchConnectivity messages are not designed for 100 Hz real-time streams; this would
  also need an iPhone app. Possible future work, not a drop-in fix.

## Latency and connection parameters

The watch (central) chooses the connection interval; Windows can only ask. With **Request a low-latency connection**
on (Device pairing page), the receiver calls `BluetoothLEDevice.RequestPreferredConnectionParameters(ThroughputOptimized)`
on Windows 11 22H2+ and logs the result and the interval Windows reports. Apple devices typically use 15–30 ms
intervals; several samples are delivered per connection event, which is why the receiver filters with the watch's
own timestamps and glides at 240 Hz.

What's measured (Diagnostics page): sample rate, arrival interval, delay above best sample, BLE round trip (v2 watch
app), PC pipeline (arrival → cursor), timer lateness. The dashboard's latency is **RTT/2 + PC pipeline** and is
labelled as an estimate.

## Security

Without extra steps, any nearby device that speaks this protocol can move the pointer while the receiver runs (same
as the original). Mitigations:

- **Only accept input from the trusted watch** (Device pairing): filters by the name the watch reports. Stops other
  WatchPointer users; a malicious device could spoof the name.
- **Require an encrypted (bonded) link** (experimental): sets the input characteristic to `EncryptionRequired`, which
  makes the OS pair/bond the watch with Windows first. Not verified on every adapter / watchOS combination.
- Stop the receiver (dashboard) or quit the app when not in use.
