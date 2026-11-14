import CoreMotion
import Foundation
import WatchCore

/// Reads device motion (gravity + gyroscope) and feeds the aim tracker. Battery-conscious:
/// - runs only while connected and the app is in the foreground;
/// - 100 Hz while pointing, 25 Hz while paused (aim keeps tracking so resuming lines up, but cheaper).
final class MotionService: ObservableObject {
    static let pointingRate = 100.0
    static let pausedRate = 25.0

    private let manager = CMMotionManager()
    let tracker = AimTracker()
    private var flick = WristFlickDetector()
    private(set) var running = false

    /// Called on the main queue for every sample, after the tracker has been updated.
    var onSample: ((MotionSample) -> Void)?
    /// Called when an (opt-in) wrist flick is detected.
    var onFlick: ((TimeInterval) -> Void)?
    var flickEnabled = false

    var isAvailable: Bool { manager.isDeviceMotionAvailable }

    func start(rate: Double) {
        guard manager.isDeviceMotionAvailable else { return }
        manager.deviceMotionUpdateInterval = 1.0 / rate
        guard !running else { return }
        running = true
        manager.startDeviceMotionUpdates(to: .main) { [weak self] data, _ in
            guard let self, let data else { return }
            let sample = MotionSample(
                timestamp: data.timestamp,
                gravity: Vector3(data.gravity.x, data.gravity.y, data.gravity.z),
                rotationRate: Vector3(data.rotationRate.x, data.rotationRate.y, data.rotationRate.z),
                userAcceleration: Vector3(data.userAcceleration.x, data.userAcceleration.y, data.userAcceleration.z))
            tracker.update(sample)
            if flickEnabled, flick.update(sample) { onFlick?(sample.timestamp) }
            onSample?(sample)
        }
    }

    /// Changes the sampling rate without restarting (CoreMotion picks it up on the fly).
    func setRate(_ rate: Double) {
        manager.deviceMotionUpdateInterval = 1.0 / rate
    }

    func stop() {
        guard running else { return }
        running = false
        manager.stopDeviceMotionUpdates()
    }
}
