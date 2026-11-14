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
/// samples, flushes scroll, and runs the motion-gesture detectors (each can be switched off in Settings).
/// Pointing keeps tracking while buttons are used; only Pause, a drag, or lowering the watch stop the cursor.
final class Session: ObservableObject {
    let link = PCLink()
    let motion = MotionService()
    @Published var foreground = true

    private var shake = ShakeDetector()
    private var flick = FlickDetector()
    private var twist = TwistDetector()
    private var lower = LowerDetector()
    private var lastSample: MotionSample?
    private var shakeEnabled = true, flicksEnabled = false, twistEnabled = false, lowerEnabled = true

    init() {
        motion.onSample = { [weak self] sample in self?.handle(sample) }
        applySettings()
    }

    private func handle(_ sample: MotionSample) {
        lastSample = sample
        guard link.connected else { return }
        link.flushScroll()

        if lowerEnabled, let lowered = lower.update(sample) { link.setLowered(lowered) }
        guard !link.paused, !link.lowered else { return }

        if shakeEnabled, shake.update(sample) { link.sendGesture(.shake, at: sample.timestamp) }
        if flicksEnabled, let g = flick.update(sample) { link.sendGesture(g, at: sample.timestamp) }
        if twistEnabled, let g = twist.update(sample) { link.sendGesture(g, at: sample.timestamp) }

        link.sendAim(leftDegrees: motion.tracker.leftDegrees, upDegrees: motion.tracker.upDegrees, at: sample.timestamp)
    }

    func applySettings() {
        let defaults = UserDefaults.standard
        shakeEnabled = defaults.bool(forKey: WatchSettingKey.shake)
        flicksEnabled = defaults.bool(forKey: WatchSettingKey.wristFlick)
        twistEnabled = defaults.bool(forKey: WatchSettingKey.twist)
        lowerEnabled = defaults.bool(forKey: WatchSettingKey.lowerToPause)
        if !lowerEnabled { lower.reset(); link.setLowered(false) }
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

    /// Whatever direction the watch points now becomes the middle of the screen (and the neutral twist angle).
    func center() {
        motion.tracker.recenter()
        if let lastSample { twist.recenter(lastSample) }
        link.send(.center)
    }

    func connectedChanged() {
        if link.connected {
            motion.tracker.recenter() // fresh connection: start centred, like the original
            if let lastSample { twist.recenter(lastSample) }
            lower.reset()
        }
        updateMotion()
    }
}
