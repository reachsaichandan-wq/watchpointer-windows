# Windows: install, build and run

## Requirements

- Windows 10 version 2004 (build 19041) or later; Windows 11 recommended (low-latency connection request needs 22H2+).
- A Bluetooth LE adapter whose driver supports the **peripheral role** — check with `watchpointer-cli ble-check`
  (see [BLUETOOTH.md](BLUETOOTH.md)).
- To build: the free [.NET 10 SDK](https://dotnet.microsoft.com/download). To only run a published build: the .NET 10
  Desktop Runtime (or use a self-contained build, below).

## Build from source

```powershell
git clone <your fork> WatchPointer
cd WatchPointer
dotnet test Tests/WatchPointer.Core.Tests          # 176 tests
dotnet run --project WindowsApp/WatchPointer.App    # starts the app
```

Or produce a portable folder:

```powershell
./build.ps1                    # framework-dependent → artifacts/WatchPointer-win-x64
./build.ps1 -SelfContained     # includes the .NET runtime (bigger, no install needed)
```

Run `artifacts\WatchPointer-win-x64\WatchPointer.exe`. Nothing is installed system-wide; settings live in
`%APPDATA%\WatchPointer\settings.json`, logs in `%LOCALAPPDATA%\WatchPointer\logs`. Delete those folders to
uninstall completely.

CI builds the same folder on every push (`Windows receiver` workflow → artifact *WatchPointer-win-x64*).

> The `.exe` is not code-signed (signing certificates cost money). Windows SmartScreen may warn on first run of a
> downloaded build: *More info → Run anyway*. Building it yourself avoids the warning.

### Start with Windows (optional)

Press `Win+R`, run `shell:startup`, and put a shortcut to `WatchPointer.exe` there. Add `--show` to the shortcut's
target if you want the window instead of starting in the notification area (when *Start minimized* is on).

## First run

1. Turn Bluetooth on.
2. Start WatchPointer. The status bar should go **Starting → Advertising**. If it says *Not supported*, read the
   message on **Device pairing**.
3. On the watch: open WatchPointer, tap **Pair** next to your PC, point at the middle of the screen, tap **Center**.
4. Optional: **Calibration** page → *Start calibration* to fit sensitivity to your wrist.

## No watch yet? Use the simulator

- App: **Diagnostics → Simulator** → choose a pattern → *Start simulator* (preview only; tick *Move the real cursor*
  to drive the mouse).
- CLI: `watchpointer-cli simulate --pattern figureeight --seconds 5` (add `--live` to move the cursor).
- Benchmark: `watchpointer-cli bench --seconds 60`.

## Test the Bluetooth link without a watch

Any BLE "GATT client" phone app can stand in for the watch, e.g. **nRF Connect** (free, iOS/Android) or **LightBlue**:

1. Run `watchpointer-cli listen --verbose` (the cursor is not touched unless you add `--move`). Close the WatchPointer
   app first: only one program should host the service.
2. In the phone app, connect to your PC, open service `6B1E0001-…`, enable notifications on `…0003`, and write hex to
   `…0002`: `0000000000` aims at the centre, `0064000000` aims 1° left, `0100000000` is a left click (only acted on
   with `--move`), `0601` is rejected and counted as invalid (bad info JSON).
3. The console prints each packet and the engine state once a second.

## Command-line reference

```
watchpointer-cli ble-check                    adapter report + real advertising test (exit 0 = OK)
watchpointer-cli listen [--move] [--seconds N] [--verbose]
watchpointer-cli simulate [--pattern circle|figureeight|sweep|holdstill|steps] [--seconds N] [--live]
watchpointer-cli bench [--seconds N]
watchpointer-cli displays
WatchPointer.exe --show                   open the window even if "start minimized" is set
WatchPointer.exe --render-pages <dir> [--light]   render every page to PNG and exit (docs/CI)
```
