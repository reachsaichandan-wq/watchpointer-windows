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

- **Mainstream sideloading tools on Windows (Sideloadly, AltStore, SideStore)** install iPhone apps with a free Apple
  ID but, as of November 2026, don't install the embedded Apple Watch app (see section 2c for details and the one
  experimental route that reportedly does).
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
   - builds the small iPhone companion app with the watch app embedded (`Payload/WatchPointerPhone.app/Watch/
     WatchPointer.app`) and checks the layout;
   - uploads `WatchPointer-unsigned-watchos.zip`, **`WatchPointer-unsigned.ipa`** and the build logs.
3. Download artifacts from the run page (Actions → the run → *Artifacts*), signed in to GitHub. The builds are
   **unsigned**: a sideloading tool or Xcode must sign them before a device accepts them.

The iPhone app does nothing except carry the watch app (the watch app runs on its own and talks to the PC directly);
it exists because sideloading tools and TestFlight work with iPhone `.ipa` files.

## 2a. Install with a Mac and a free Apple ID

Needs about 30 minutes on a Mac, then repeats every 7 days.

1. On the Mac: install Xcode (free, App Store) recent enough for your watch's watchOS version, then
   `brew install xcodegen`.
2. `git clone` your repository, `cd WatchApp`, `xcodegen generate`, open `WatchPointer.xcodeproj`.
3. Xcode › Settings › Accounts: add your Apple ID (creates a free *Personal Team*).
4. Signing & Capabilities for **both** targets: choose your Personal Team and change the bundle identifiers to
   something unique, keeping the watch's as the iPhone's plus a suffix, e.g. `com.<yourname>.watchpointer` (target
   *WatchPointerPhone*) and `com.<yourname>.watchpointer.watchkitapp` (target *WatchPointer*). Also update
   `WKCompanionAppBundleIdentifier` in `project.yml` (or the watch target's Info) to the iPhone identifier.
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

## 2c. Windows only, free Apple ID — experimental, unverified

Status in November 2026, from public reports (not tested by this project):

| Tool | Installs the embedded watch app? |
|---|---|
| Sideloadly | No (reported: free accounts don't handle watch apps) |
| SideStore | No: its provisioning profile omits the watch's device ID → install error `0xe8008015` |
| AltStore (Windows) | Reported to fail at Apple ID sign-in after Apple's October 2026 login change |
| iloader / isideload, community "Watch" branch | **One report of success** (Apple Watch Series 10, watchOS 27.2), in [HermesEnabler issue #54](https://github.com/pookjw/HermesEnabler/issues/54), 12 Nov 2026 |

That report's route, in short (read the issue for the details; it's the author's own account):

1. Install iTunes (for Apple's USB drivers). iPhone: Developer Mode on, connected by USB. Watch: Developer Mode on
   (the author used an experimental script from a third-party `xtool` branch to reveal the setting).
2. Build the installer: the community iloader branch with isideload patched so the watch app's provisioning profile
   includes the watch, compiled into a small command-line tool. There is no ready-made download.
3. Run the tool on the **unmodified** `.ipa` (`WatchPointer-unsigned.ipa` from CI), sign in with the Apple ID and
   2-factor code. If it reports error 7460 (certificate limit), the free account's existing development certificate
   must be revoked — which stops other apps signed with it (e.g. AltStore) until they're re-signed.
4. Re-run every 7 days (free certificates expire).

**Security:** these tools send your Apple ID password and 2-factor code from third-party code, and that route used a
third-party "anisette" server. Use a secondary Apple ID, not your main one. Nothing here is endorsed by Apple, and it
can break whenever Apple changes its sign-in or provisioning services.

If you try it with WatchPointer, please report the result (issue on this repository) so this table can be updated.

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
