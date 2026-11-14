# Apple Watch app: building and installing

**Short version:** building is free and needs no Mac (GitHub Actions). **Installing on a real watch needs code
signing**, and Apple only offers two routes: Xcode on a Mac (free Apple ID works, apps expire after 7 days), or a
paid Apple Developer Program membership (then CI can deliver through TestFlight with no Mac at all). There is no
verified free way to install a watchOS app from Windows.

You can try the Windows side without any of this: the original WatchPointer watch app also works with this receiver,
and the receiver has a built-in simulator.

## What needs what

| Step | Free? | Needs |
|---|---|---|
| Build the Windows app, run tests, simulator | Yes | Windows 10 2004+ / 11, .NET 10 SDK |
| Use the receiver with a watch | Yes | A Bluetooth LE adapter that supports the **peripheral role** (`watchpointer-cli ble-check`) |
| Build the watch app (compile check, unsigned `.app`) | Yes (public repo) | A GitHub account; GitHub-hosted macOS runners are free for public repositories under GitHub's current terms. Private repos use paid/included minutes. |
| Run the watch app's logic tests | Yes | Same CI (Linux job and macOS job) |
| Install on your watch, free | Free of charge | A Mac you can use (borrowed is fine) with a current Xcode, a free Apple ID, your iPhone and watch, Developer Mode on both. Re-install every 7 days. |
| Install on your watch, no Mac | **Paid** | Apple Developer Program membership; the optional `watchos-testflight.yml` workflow; TestFlight on the iPhone |
| Apple Watch itself | — | watchOS 11 or later. An iPhone is required by Apple to set up and pair any Apple Watch, and it is the bridge Xcode/TestFlight use to install apps |

Not supported / not recommended:

- **Sideloading tools on Windows (AltStore, Sideloadly, …)** install iOS apps with a free Apple ID. Installing a
  watch-only watchOS app this way is not something this project has verified, and these tools have historically not
  supported Apple Watch apps. Don't count on it.
- **macOS virtual machines on non-Apple hardware** are against Apple's macOS licence. Not documented here.
- **Xcode Cloud / paid cloud Macs**: work, but cost money; not required by this project.

## 1. Build on GitHub Actions (free, no Mac)

1. Push this repository to a **public** GitHub repository.
2. The `watchOS app` workflow (`.github/workflows/watchos.yml`) runs on every change under `WatchApp/` or `Shared/`
   (or start it from the Actions tab → *Run workflow*). It:
   - runs `swift test` for `WatchCore` on Linux and macOS (protocol vectors shared with the Windows tests, aim math,
     gesture classifier);
   - installs [XcodeGen](https://github.com/yonaskolb/XcodeGen) and generates `WatchPointer.xcodeproj` from
     `WatchApp/project.yml`;
   - builds for the watchOS Simulator and for a generic watchOS device with signing disabled;
   - uploads `WatchPointer-unsigned-watchos.zip` and the build logs.
3. The unsigned app proves the code compiles. **It can't be installed on a watch as is.**

## 2a. Install with a Mac and a free Apple ID

Needs about 30 minutes on a Mac, then repeats every 7 days.

1. On the Mac: install Xcode (free, App Store) recent enough for your watch's watchOS version, then
   `brew install xcodegen`.
2. `git clone` your repository, `cd WatchApp`, `xcodegen generate`, open `WatchPointer.xcodeproj`.
3. Xcode › Settings › Accounts: add your Apple ID (creates a free *Personal Team*).
4. Target *WatchPointer* › Signing & Capabilities: choose your Personal Team; change the bundle identifier to
   something unique, e.g. `com.<yourname>.watchpointer`.
5. Enable Developer Mode: on the iPhone (Settings › Privacy & Security › Developer Mode) and on the watch
   (Settings › Privacy & Security › Developer Mode). Connect the iPhone to the Mac; keep the watch near it, unlocked.
6. Select your Apple Watch as the run destination and press ⌘R. The first install can take several minutes.
7. If the watch says the developer isn't trusted: on the iPhone, Settings › General › VPN & Device Management.

Free-provisioning limits (Apple's current rules): the app stops launching after **7 days** (rebuild to renew), a
limited number of apps and App IDs per week, no TestFlight / App Store distribution.

## 2b. Install with no Mac (paid membership, unverified here)

`.github/workflows/watchos-testflight.yml` archives with automatic signing using an App Store Connect API key and
uploads to TestFlight. Set the secrets listed at the top of the workflow, create the app record in App Store Connect,
run the workflow, then install from the TestFlight app on your iPhone (watch apps appear there). This project could
not test it without a paid account, so treat it as a starting point.

## 3. First run

1. Start WatchPointer on the PC (it must say **Advertising**).
2. Open WatchPointer on the watch; allow Bluetooth.
3. Tap **Pair** next to your PC. A PC that has never connected may be listed as "Windows PC"; after the first
   connection the watch shows the real name (read from the v2 info characteristic).
4. Point at the middle of the screen, tap **Center** (target icon).

Watch controls: pad tap = click, hold 0.35 s = drag, swipe = gesture, bottom row = right click / center / pause,
Crown = scroll, bottom toolbar = remote buttons A–D, Double Tap button (pinch twice on Series 9 / Ultra 2 and
later), settings.

## Using the original watch app instead

The original app (Watch/ in [umarz317/WatchPointer](https://github.com/umarz317/WatchPointer)) speaks protocol v1,
which this receiver implements fully. You get pointing, click, drag, right click, scroll, center, pause and battery.
Swipes, remote buttons, Double Tap, the PC's profile name and RTT latency need this project's watch app. Installing it
has the same signing requirements as above.

## Battery

The app samples motion only while connected and in the foreground (100 Hz pointing, 25 Hz paused), stops scanning
once it knows its PC, and sends battery info once a minute. The screen-on time dominates; the tips on the
Calibration page (wake duration, return-to-clock) trade battery for convenience.
