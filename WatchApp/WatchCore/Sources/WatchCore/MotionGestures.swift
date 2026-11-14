import Foundation

/// Motion gestures that ordinary pointing doesn't produce, so they can be told apart reliably.
/// Each detector is pure and fed CoreMotion samples (`MotionSample`).

/// Rapid back-and-forth shake: several sign reversals of strong acceleration along one axis within a short window.
/// Pointing never reverses ±1 g repeatedly within a fraction of a second, so this is very unlikely to misfire.
public struct ShakeDetector {
    public var threshold = 1.0          // g, per peak
    public var reversalsNeeded = 3      // 4 alternating peaks
    public var maxGap: TimeInterval = 0.25
    public var refractory: TimeInterval = 1.0
    private var lastPeak: (axis: Int, sign: Int, time: TimeInterval)?
    private var reversals = 0
    private var inPeak = false
    private var lastFire: TimeInterval = -.infinity

    public init() {}

    public mutating func update(_ s: MotionSample) -> Bool {
        let a = s.userAcceleration
        let components = [a.x, a.y, a.z]
        let axis = components.indices.max { abs(components[$0]) < abs(components[$1]) } ?? 0
        let value = components[axis]
        guard abs(value) > threshold else { inPeak = false; return false }
        if inPeak { return false } // still the same peak
        inPeak = true
        let sign = value > 0 ? 1 : -1
        if let last = lastPeak, last.axis == axis, last.sign != sign, s.timestamp - last.time <= maxGap {
            reversals += 1
        } else {
            reversals = 0
        }
        lastPeak = (axis: axis, sign: sign, time: s.timestamp)
        guard reversals >= reversalsNeeded, s.timestamp - lastFire > refractory else { return false }
        reversals = 0
        lastFire = s.timestamp
        return true
    }
}

/// Sharp, short turn of the wrist left or right and back: a spike in rotation speed around the vertical that is much
/// faster than pointing (default 6 rad/s ≈ 340°/s) and over within 0.2 s. Direction = the first swing.
public struct FlickDetector {
    public var peakRate = 6.0                // rad/s
    public var activeRate = 2.5              // rad/s: the spike's extent
    public var maxDuration: TimeInterval = 0.2
    public var refractory: TimeInterval = 0.6
    private var spikeStart: TimeInterval?
    private var firstSign = 0
    private var reachedPeak = false
    private var lastFire: TimeInterval = -.infinity

    public init() {}

    /// Returns `.flickLeft` / `.flickRight` once per flick, when the spike ends.
    public mutating func update(_ s: MotionSample) -> WatchGesture? {
        let n = max(s.gravity.length, 1e-6)
        let up = Vector3(-s.gravity.x / n, -s.gravity.y / n, -s.gravity.z / n)
        let yawRate = s.rotationRate.dot(up) // positive = turning left (same convention as AimTracker)
        if abs(yawRate) > activeRate {
            if spikeStart == nil { spikeStart = s.timestamp; firstSign = yawRate > 0 ? 1 : -1; reachedPeak = false }
            if abs(yawRate) > peakRate { reachedPeak = true }
            return nil
        }
        guard let start = spikeStart else { return nil }
        spikeStart = nil
        guard reachedPeak, s.timestamp - start <= maxDuration, s.timestamp - lastFire > refractory else { return nil }
        lastFire = s.timestamp
        return firstSign > 0 ? .flickLeft : .flickRight
    }
}

/// Twisting the wrist (roll around the 12 o'clock axis) past `threshold` from the neutral roll. Fires once when the
/// threshold is crossed, then repeats while held (for volume and similar). Roll doesn't change the aim, so twisting
/// doesn't move the pointer.
public struct TwistDetector {
    public var threshold = 35.0 * .pi / 180
    public var release = 20.0 * .pi / 180
    public var repeatInterval: TimeInterval = 0.35
    public var neutralRoll = 0.0
    private var heldSign = 0
    private var nextRepeat: TimeInterval = 0

    public init() {}

    /// Roll angle of the watch, radians; positive = clockwise as seen looking along the 12 o'clock edge.
    public static func roll(_ s: MotionSample) -> Double {
        let n = max(s.gravity.length, 1e-6)
        let up = Vector3(-s.gravity.x / n, -s.gravity.y / n, -s.gravity.z / n)
        return atan2(-up.x, up.z)
    }

    public mutating func recenter(_ s: MotionSample) { neutralRoll = Self.roll(s); heldSign = 0 }

    public mutating func update(_ s: MotionSample) -> WatchGesture? {
        let angle = remainder(Self.roll(s) - neutralRoll, 2 * .pi)
        if abs(angle) < release { heldSign = 0; return nil }
        guard abs(angle) > threshold else { return nil }
        let sign = angle > 0 ? 1 : -1
        if sign != heldSign {
            heldSign = sign
            nextRepeat = s.timestamp + repeatInterval * 1.5 // a slightly longer first delay, like key repeat
            return sign > 0 ? .twistClockwise : .twistAnticlockwise
        }
        guard s.timestamp >= nextRepeat else { return nil }
        nextRepeat = s.timestamp + repeatInterval
        return sign > 0 ? .twistClockwise : .twistAnticlockwise
    }
}

/// Lowered = the 12 o'clock edge points at the floor (arm hanging down). With hysteresis and a short hold so a quick
/// downward point at the taskbar doesn't trigger it.
public struct LowerDetector {
    public var lowerBelow = -55.0 * .pi / 180
    public var raiseAbove = -35.0 * .pi / 180
    public var hold: TimeInterval = 0.4
    public private(set) var lowered = false
    private var candidateSince: TimeInterval?

    public init() {}

    /// Returns the new state when it changes (true = just lowered, false = just raised), else nil.
    public mutating func update(_ s: MotionSample) -> Bool? {
        let n = max(s.gravity.length, 1e-6)
        let elevation = asin(min(max(-s.gravity.y / n, -1), 1))
        let wantsChange = lowered ? elevation > raiseAbove : elevation < lowerBelow
        guard wantsChange else { candidateSince = nil; return nil }
        let since = candidateSince ?? s.timestamp
        candidateSince = since
        guard s.timestamp - since >= (lowered ? 0.1 : hold) else { return nil }
        lowered.toggle()
        candidateSince = nil
        return lowered
    }

    public mutating func reset() { lowered = false; candidateSince = nil }
}
