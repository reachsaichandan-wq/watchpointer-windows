import SwiftUI
import WatchCore

@main
struct WatchPointerApp: App {
    init() { WatchSettingDefaults.register() }

    var body: some Scene {
        WindowGroup { ContentView() }
    }
}

/// Glue between Bluetooth, motion and the UI: starts motion only while connected and in the foreground, sends aim
/// samples, flushes scroll, and fires wrist-flick gestures when enabled.
final class Session: ObservableObject {
    let link = PCLink()
    let motion = MotionService()
    @Published var foreground = true

    init() {
        motion.onSample = { [weak self] sample in
            guard let self, link.connected else { return }
            link.flushScroll()
            guard !link.paused else { return }
            link.sendAim(leftDegrees: motion.tracker.leftDegrees, upDegrees: motion.tracker.upDegrees, at: sample.timestamp)
        }
        motion.onFlick = { [weak self] time in
            guard let self, link.connected, !link.paused else { return }
            link.sendGesture(.wristFlick, at: time)
        }
        applySettings()
    }

    func applySettings() {
        let defaults = UserDefaults.standard
        motion.flickEnabled = defaults.bool(forKey: WatchSettingKey.wristFlick)
        motion.tracker.driftCompensation = defaults.bool(forKey: WatchSettingKey.driftCompensation)
    }

    /// Call whenever connection, pause or foreground state changes.
    func updateMotion() {
        if link.connected && foreground {
            motion.start(rate: link.paused ? MotionService.pausedRate : MotionService.pointingRate)
            motion.setRate(link.paused ? MotionService.pausedRate : MotionService.pointingRate)
        } else {
            motion.stop()
        }
    }

    /// Whatever direction the watch points now becomes the middle of the screen.
    func center() {
        motion.tracker.recenter()
        link.send(.center)
    }

    func connectedChanged() {
        if link.connected { motion.tracker.recenter() } // fresh connection: start centred, like the original
        updateMotion()
    }
}
