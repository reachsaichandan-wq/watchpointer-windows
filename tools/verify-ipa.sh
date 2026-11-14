#!/usr/bin/env bash
# Verifies that an .ipa carries a watch app that iOS will actually offer on a paired Apple Watch.
# Usage: tools/verify-ipa.sh WatchPointer.ipa      (macOS: uses plutil, lipo)
# Exit code 0 = all checks passed. Prints the full bundle hierarchy and the relevant Info.plist keys.
set -euo pipefail
IPA="$1"
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT
unzip -q "$IPA" -d "$WORK"

fail=0
check() { if eval "$2"; then echo "  PASS  $1"; else echo "  FAIL  $1"; fail=1; fi; }
key() { /usr/libexec/PlistBuddy -c "Print :$2" "$1/Info.plist" 2>/dev/null || echo "<missing>"; }

echo "== Bundle hierarchy"
(cd "$WORK" && find . -print | sort | sed 's|^\./||')

APP=$(find "$WORK/Payload" -maxdepth 1 -name "*.app" | head -n 1)
WATCH_DIR="$APP/Watch"
WATCH=$(find "$WATCH_DIR" -maxdepth 1 -name "*.app" 2>/dev/null | head -n 1 || true)

echo "== iPhone app: ${APP#$WORK/}"
for k in CFBundleIdentifier CFBundleShortVersionString CFBundleVersion MinimumOSVersion DTPlatformName; do echo "  $k = $(key "$APP" $k)"; done
echo "== Watch app: ${WATCH#$WORK/}"
if [ -n "$WATCH" ]; then
  for k in CFBundleIdentifier WKApplication WKCompanionAppBundleIdentifier WKRunsIndependentlyOfCompanionApp WKWatchOnly WKWatchKitApp MinimumOSVersion DTPlatformName UIDeviceFamily; do
    echo "  $k = $(key "$WATCH" $k | tr '\n' ' ')"
  done
  EXE="$WATCH/$(key "$WATCH" CFBundleExecutable)"
  echo "  architectures: $(lipo -archs "$EXE")"
fi

IOS_ID=$(key "$APP" CFBundleIdentifier)
echo "== Checks"
check "Payload contains exactly one iOS .app" '[ "$(find "$WORK/Payload" -maxdepth 1 -name "*.app" | wc -l | tr -d " ")" = 1 ]'
check "iOS app platform is iphoneos" '[ "$(key "$APP" DTPlatformName)" = iphoneos ]'
check "watch app embedded at <iOS app>/Watch/*.app" '[ -n "$WATCH" ] && [ -d "$WATCH" ]'
if [ -n "$WATCH" ]; then
  W_ID=$(key "$WATCH" CFBundleIdentifier)
  check "watch app platform is watchos" '[ "$(key "$WATCH" DTPlatformName)" = watchos ]'
  check "watch app is a single-target watchOS app (WKApplication = true)" '[ "$(key "$WATCH" WKApplication)" = true ]'
  check "watch app is a companion app, not watch-only (no WKWatchOnly)" '[ "$(key "$WATCH" WKWatchOnly)" = "<missing>" ]'
  check "no legacy WatchKit extension needed (no PlugIns/*.appex)" '[ ! -d "$WATCH/PlugIns" ] || [ -z "$(ls -A "$WATCH/PlugIns")" ]'
  check "WKCompanionAppBundleIdentifier ($(key "$WATCH" WKCompanionAppBundleIdentifier)) == iOS bundle ID ($IOS_ID)" '[ "$(key "$WATCH" WKCompanionAppBundleIdentifier)" = "$IOS_ID" ]'
  check "watch bundle ID ($W_ID) starts with iOS bundle ID + '.'" '[[ "$W_ID" == "$IOS_ID".* ]]'
  check "watch binary has an arm64 slice (Series 9 and later) and arm64_32 (earlier)" 'lipo -archs "$EXE" | grep -qw arm64 && lipo -archs "$EXE" | grep -qw arm64_32'
  check "same marketing version on both" '[ "$(key "$APP" CFBundleShortVersionString)" = "$(key "$WATCH" CFBundleShortVersionString)" ]'
fi
echo "== Signing (an unsigned .ipa is expected; the signing tool must sign BOTH apps)"
for b in "$APP" "$WATCH"; do
  [ -n "$b" ] || continue
  if codesign -dv "$b" >/dev/null 2>&1; then echo "  signed:   ${b#$WORK/}"; else echo "  unsigned: ${b#$WORK/}"; fi
  [ -f "$b/embedded.mobileprovision" ] && echo "    has embedded.mobileprovision" || true
done
exit $fail
