import Foundation

/// What a touch on the click pad turned out to be.
public enum PadEvent: Equatable {
    /// Short touch without movement. `touchDown` is when the thumb landed (the PC rewinds the pointer to then).
    case tap(touchDown: TimeInterval)
    /// Held still past the hold delay: mouse button down at touch-down position.
    case holdStart(touchDown: TimeInterval)
    /// Released after a hold: mouse button up, just before the lift.
    case holdEnd(at: TimeInterval)
    case swipe(WatchGesture, at: TimeInterval)
}

/// Classifies pad touches into tap / hold (drag) / swipe. Pure state machine; the view feeds it touch events and a
/// periodic `tick` (for the hold timer). Rules:
/// - moving more than `moveTolerance` before the hold fires cancels tap and hold; on release it's a swipe if it
///   travelled `swipeDistance` along one axis within `swipeMaxDuration`, otherwise nothing (no accidental click);
/// - once holding, movement is ignored (the wrist steers the drag, not the thumb).
public struct PadGestureClassifier {
    public var holdDelay: TimeInterval = 0.35 // same as the original app
    public var moveTolerance = 10.0          // points
    public var swipeDistance = 28.0          // points
    public var swipeMaxDuration: TimeInterval = 0.6
    public var swipesEnabled = true

    private enum State: Equatable {
        case idle
        case pressing(down: TimeInterval, startX: Double, startY: Double, moved: Bool)
        case holding
    }

    private var state = State.idle

    public init() {}

    public var isHolding: Bool { state == .holding }
    public var isPressing: Bool { if case .pressing = state { return true }; return state == .holding }

    public mutating func touchDown(at time: TimeInterval, x: Double, y: Double) -> [PadEvent] {
        state = .pressing(down: time, startX: x, startY: y, moved: false)
        return []
    }

    public mutating func touchMoved(at time: TimeInterval, x: Double, y: Double) -> [PadEvent] {
        guard case let .pressing(down, sx, sy, moved) = state, !moved else { return tick(at: time) }
        let distance = ((x - sx) * (x - sx) + (y - sy) * (y - sy)).squareRoot()
        if distance > moveTolerance {
            state = .pressing(down: down, startX: sx, startY: sy, moved: true)
            return []
        }
        return tick(at: time)
    }

    public mutating func tick(at time: TimeInterval) -> [PadEvent] {
        guard case let .pressing(down, _, _, moved) = state, !moved, time - down >= holdDelay else { return [] }
        state = .holding
        return [.holdStart(touchDown: down)]
    }

    public mutating func touchUp(at time: TimeInterval, x: Double, y: Double) -> [PadEvent] {
        defer { state = .idle }
        switch state {
        case .holding:
            return [.holdEnd(at: time - 0.05)] // just before the lift
        case let .pressing(down, sx, sy, moved):
            if !moved {
                // A hold that the timer didn't catch yet still counts as a hold of zero length → a click.
                return time - down >= holdDelay ? [.holdStart(touchDown: down), .holdEnd(at: time - 0.05)] : [.tap(touchDown: down)]
            }
            guard swipesEnabled, time - down <= swipeMaxDuration else { return [] }
            let dx = x - sx, dy = y - sy
            if abs(dx) >= abs(dy), abs(dx) >= swipeDistance { return [.swipe(dx < 0 ? .swipeLeft : .swipeRight, at: time)] }
            if abs(dy) > abs(dx), abs(dy) >= swipeDistance { return [.swipe(dy < 0 ? .swipeUp : .swipeDown, at: time)] } // screen y grows down
            return []
        case .idle:
            return []
        }
    }

    public mutating func cancel() { state = .idle }
}

/// Opt-in wrist flick: a short, sharp spike of user acceleration. Off by default because ordinary pointing can come
/// close; the threshold, the maximum spike length and a refractory period keep false triggers rare.
public struct WristFlickDetector {
    public var threshold = 1.6           // g of user acceleration
    public var maxSpike: TimeInterval = 0.15
    public var refractory: TimeInterval = 0.8
    private var spikeStart: TimeInterval?
    private var lastFire: TimeInterval = -.infinity
    private var spikeTooLong = false

    public init() {}

    /// Returns true once per flick, when the spike ends.
    public mutating func update(_ sample: MotionSample) -> Bool {
        let t = sample.timestamp
        let above = sample.userAcceleration.length > threshold
        if above {
            if spikeStart == nil { spikeStart = t; spikeTooLong = false }
            if let s = spikeStart, t - s > maxSpike { spikeTooLong = true }
            return false
        }
        guard let start = spikeStart else { return false }
        spikeStart = nil
        guard !spikeTooLong, t - start <= maxSpike + 0.02, t - lastFire > refractory else { return false }
        lastFire = t
        return true
    }
}

/// Each Crown event is a tiny fraction; keep the leftovers so slow turns still add up to scrolling.
public struct ScrollAccumulator {
    public private(set) var pending = 0.0
    public init() {}
    public mutating func add(_ pixels: Double) { pending += pixels }
    /// Whole pixels ready to send (0 if none), removing them from the pending amount.
    public mutating func takeWhole() -> Int16 {
        let whole = Int16(clamping: Int(pending))
        pending -= Double(whole)
        return whole
    }
    public mutating func reset() { pending = 0 }
}
