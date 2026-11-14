# Troubleshooting

Start with **Diagnostics → Copy report**; it includes adapter capabilities, live metrics and the last log lines.
Logs: `%LOCALAPPDATA%\WatchPointer\logs`.

## Bluetooth / connection

| Symptom | Cause | Fix |
|---|---|---|
| Status *Not supported* | Adapter/driver lacks LE peripheral role | [BLUETOOTH.md](BLUETOOTH.md): update driver or use another adapter. `watchpointer-cli ble-check` shows details. |
| Status *Bluetooth off* | Radio off | Turn Bluetooth on; the receiver resumes by itself. |
| Status *Retrying* | Advertising failed or was aborted (driver reset, sleep/resume) | Waits 1→30 s between attempts. *Device pairing → Re-check adapter and restart* forces a retry. |
| Watch shows no PCs | Receiver not advertising; watch Bluetooth off; too far | Check status bar says *Advertising*. Restart the receiver. Move closer. |
| PC listed as "Windows PC" | Windows doesn't advertise the computer name | Pair anyway; the real name appears after the first connection. |
| Watch connects then drops after ~20 s | Watch app suspended while the link stays up | Keep the watch awake (wake duration 70 s, return-to-clock 1 h). Logged as "connected but silent". |
| Paired with the wrong PC | Pairing is remembered on the watch | Watch: tap the PC icon (top-left) → Unpair, or Settings → Forget. |
| "Untrusted watch" | *Only accept the trusted watch* is on and another watch connected | Device pairing → *Trust this watch*, or turn the filter off. |
| Encrypted link option: watch can't connect | Bonding between watchOS and Windows failed on this adapter | Turn *Require an encrypted link* off and restart the receiver. |

## Pointer behaviour

| Symptom | Fix |
|---|---|
| Cursor drifts slowly left/right | Heading is integrated from the gyro and drifts a little. Tap **Center**, press Ctrl+Alt+C, enable *Re-anchor at screen edges* (Pointer settings), or *Drift trim* on the watch. Up/down can't drift. |
| Too sensitive / not enough | Pointer settings → horizontal/vertical speed, or run Calibration. |
| Jittery when holding still | Lower *Steadiness when still* (e.g. 0.5 Hz). |
| Laggy on fast moves | Raise *Quickness* (e.g. 0.02–0.03). |
| Clicks land slightly off | Rare with the new app (clicks carry touch-down time). With older watch apps the receiver rewinds 0.1 s; hold still while tapping. |
| Pointer stops at a screen edge on multi-monitor | With *Primary display* the projection is centred on the primary; point further, or choose *All displays* as pointing area. |
| Wrong position with mixed scaling | Make sure you run the app's own `.exe` (it's per-monitor DPI aware). Report your `watchpointer-cli displays` output. |
| Cursor moves but clicks/keys do nothing in one window | That window is elevated (admin): Windows blocks input from normal apps (UIPI). Run WatchPointer as administrator only if you need this. |
| Ctrl+Alt+Del, Win+L, UAC prompts don't react | Windows never accepts injected input for the secure desktop. Not fixable by apps. |
| Slide keys go to the wrong app | Keyboard shortcuts go to the focused window: click into the slideshow once. Media keys work regardless of focus. |
| Hotkey says "already used by another app" | Choose another combination on Keyboard shortcuts. |

## Watch app

| Symptom | Fix |
|---|---|
| Watch locks when taken off the wrist | iPhone Watch app → Passcode → turn off Wrist Detection. |
| App returns to the watch face | Watch: General › Return to Clock › WatchPointer → 1 hour; Display & Brightness › Wake Duration → 70 s. |
| Double Tap button does nothing when pinching | System Double Tap needs Series 9 / Ultra 2 or newer, with Double Tap enabled in watch Settings › Gestures. |
| Swipes trigger instead of clicks | Settings on the watch → turn off *Swipes on the pad*. |
| Wrist flick fires accidentally | It's off by default; turn it off again in watch Settings. |
| App stops launching after a week | Free-provisioning profiles expire after 7 days; rebuild from Xcode ([WATCH_INSTALL.md](WATCH_INSTALL.md)). |

## Build

| Symptom | Fix |
|---|---|
| `dotnet` not found | Install the .NET 10 SDK; open a new terminal. |
| NuGet restore fails | Check internet access to nuget.org; `dotnet nuget locals all --clear`. |
| watchOS CI fails at "Generate Xcode project" | XcodeGen output changed; check the log artifact and `WatchApp/project.yml`. |
| watchOS CI: no watchOS SDK | The runner image's default Xcode lacks it; add an `xcode-select` step for an Xcode that has it (see the "Show Xcode and SDKs" step output). |
