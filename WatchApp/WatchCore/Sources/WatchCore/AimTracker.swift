import Foundation

public struct Vector3: Equatable {
    public var x: Double, y: Double, z: Double
    public init(_ x: Double, _ y: Double, _ z: Double) { self.x = x; self.y = y; self.z = z }
    public var length: Double { (x * x + y * y + z * z).squareRoot() }
    public func dot(_ o: Vector3) -> Double { x * o.x + y * o.y + z * o.z }
}

/// One CoreMotion device-motion sample, without the CoreMotion types (so this file is testable on any OS).
public struct MotionSample {
    public var timestamp: TimeInterval
    /// Gravity in the device frame, in g (CMDeviceMotion.gravity).
    public var gravity: Vector3
    /// Bias-corrected rotation rate in the device frame, rad/s (CMDeviceMotion.rotationRate).
    public var rotationRate: Vector3
    /// Acceleration without gravity, in g (CMDeviceMotion.userAcceleration).
    public var userAcceleration: Vector3

    public init(timestamp: TimeInterval, gravity: Vector3, rotationRate: Vector3, userAcceleration: Vector3 = Vector3(0, 0, 0)) {
        self.timestamp = timestamp
        self.gravity = gravity
        self.rotationRate = rotationRate
        self.userAcceleration = userAcceleration
    }
}

/// Where the watch's 12 o'clock edge points, relative to "Center". Same model as the original WatchPointer:
/// - up/down comes straight from gravity, so it never drifts;
/// - left/right has no absolute reference, so every turn around the vertical axis is added up (gyro integration).
///   No dead zone by default: dropping slow turns is what made early versions slide out of line.
public final class AimTracker {
    /// Radians turned left since Center (unbounded; wrapped when reported).
    public private(set) var heading = 0.0
    /// Radians the top edge points above horizontal, right now.
    public private(set) var elevation = 0.0
    private var elevationZero = 0.0
    private var lastTimestamp: TimeInterval?

    /// Opt-in residual-bias trim: while the watch is held very still, slowly learn the leftover vertical rotation
    /// rate and subtract it. CoreMotion already removes most gyro bias, so this is off by default.
    public var driftCompensation = false
    public private(set) var estimatedBias = 0.0 // rad/s
    private var stillSince: TimeInterval?

    /// Longest gap integrated in one step (the original clamps to 50 ms so a stall can't fling the pointer).
    public static let maxStep: TimeInterval = 0.05
    static let stillRate = 0.03          // rad/s ≈ 1.7 °/s
    static let stillDelay: TimeInterval = 1.0
    static let maxBias = 0.01            // rad/s ≈ 0.6 °/s

    public init() {}

    public func update(_ sample: MotionSample) {
        let dt = lastTimestamp.map { min(max(sample.timestamp - $0, 0), Self.maxStep) } ?? 0
        lastTimestamp = sample.timestamp

        let n = max(sample.gravity.length, 1e-6)
        let up = Vector3(-sample.gravity.x / n, -sample.gravity.y / n, -sample.gravity.z / n)
        elevation = asin(min(max(up.y, -1), 1))
        let verticalRate = sample.rotationRate.dot(up)

        if driftCompensation {
            if sample.rotationRate.length < Self.stillRate {
                let since = stillSince ?? sample.timestamp
                stillSince = since
                if sample.timestamp - since > Self.stillDelay {
                    estimatedBias += 0.02 * (verticalRate - estimatedBias)
                    estimatedBias = min(max(estimatedBias, -Self.maxBias), Self.maxBias)
                }
            } else {
                stillSince = nil
            }
            heading += (verticalRate - estimatedBias) * dt
        } else {
            heading += verticalRate * dt
        }
    }

    /// Whatever direction the watch points now becomes the middle of the screen.
    public func recenter() {
        heading = 0
        elevationZero = elevation
    }

    /// Degrees turned left since Center, wrapped to ±180°.
    public var leftDegrees: Double { remainder(heading, 2 * .pi) * 180 / .pi }
    /// Degrees tipped up since Center.
    public var upDegrees: Double { (elevation - elevationZero) * 180 / .pi }
}
