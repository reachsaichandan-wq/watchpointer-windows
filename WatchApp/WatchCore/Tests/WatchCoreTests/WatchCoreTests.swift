import Foundation
import XCTest
@testable import WatchCore

/// Uses the same Shared/Protocol/test-vectors.json as the C# tests, so both sides provably agree byte for byte.
final class ProtocolVectorTests: XCTestCase {
    private func vectors() throws -> [String: Any] {
        let url = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent() // WatchCore
            .deletingLastPathComponent().deletingLastPathComponent()                           // repo root
            .appendingPathComponent("Shared/Protocol/test-vectors.json")
        let data = try Data(contentsOf: url)
        return try XCTUnwrap(JSONSerialization.jsonObject(with: data) as? [String: Any])
    }

    private func bytes(hex: String) -> [UInt8] {
        var out: [UInt8] = []
        var index = hex.startIndex
        while index < hex.endIndex {
            let next = hex.index(index, offsetBy: 2)
            out.append(UInt8(hex[index..<next], radix: 16)!)
            index = next
        }
        return out
    }

    func testUuidsMatchTheOriginal() {
        XCTAssertEqual(WatchPointerUUID.service, "6B1E0001-9F4C-4C2A-9B8E-3D2A1C0F5E01")
        XCTAssertEqual(WatchPointerUUID.input, "6B1E0002-9F4C-4C2A-9B8E-3D2A1C0F5E01")
        XCTAssertEqual(WatchPointerUUID.output, "6B1E0003-9F4C-4C2A-9B8E-3D2A1C0F5E01")
    }

    func testEncoderReproducesEveryFixedVector() throws {
        let valid = try XCTUnwrap(vectors()["valid"] as? [[String: Any]])
        var checked = 0
        for v in valid {
            guard let hex = v["hex"] as? String, let expect = v["expect"] as? [String: Any] else { continue }
            let expected = bytes(hex: hex)
            let kind = try XCTUnwrap(PacketKind(rawValue: expected[0]))
            let a = Int16(try XCTUnwrap(expect["a"] as? Int))
            let b = Int16(try XCTUnwrap(expect["b"] as? Int))
            let ms = (expect["ms"] as? Int).map { UInt16($0) }
            XCTAssertEqual(PacketEncoder.packet(kind, a, b, ms: ms), expected, v["name"] as? String ?? "")
            checked += 1
        }
        XCTAssertGreaterThan(checked, 10)
    }

    func testNotificationsDecode() throws {
        let notifications = try XCTUnwrap(vectors()["notifications"] as? [[String: Any]])
        for n in notifications {
            let decoded = PCNotification.decode(bytes(hex: n["hex"] as! String))
            let expect = n["expect"] as! [String: Any]
            if expect["type"] as? String == "ping" {
                XCTAssertEqual(decoded, .ping(sequence: UInt16(expect["seq"] as! Int)))
            } else {
                let flags = expect["flags"] as! Int
                XCTAssertEqual(decoded, .state(paused: flags & 1 != 0, laser: flags & 2 != 0, profile: expect["profile"] as! String))
            }
        }
    }

    func testCentidegreesRoundAndClampLikeTheReceiver() {
        XCTAssertEqual(PacketEncoder.centidegrees(10.5), 1050)
        XCTAssertEqual(PacketEncoder.centidegrees(-5.256), -526)
        XCTAssertEqual(PacketEncoder.centidegrees(0.125), 13)
        XCTAssertEqual(PacketEncoder.centidegrees(400), Int16.max)
        XCTAssertEqual(PacketEncoder.centidegrees(-400), Int16.min)
    }

    func testWatchMsWraps() {
        XCTAssertEqual(PacketEncoder.watchMs(1.234), 1234)
        XCTAssertEqual(PacketEncoder.watchMs(65.536), 0)
        XCTAssertEqual(PacketEncoder.watchMs(65.537), 1)
    }

    func testInfoPacketIsKindSixPlusJsonWithProtocolVersion() throws {
        let packet = PacketEncoder.info(model: "Watch7,5", os: "11.0", battery: 80, charging: false, name: "My Watch", appVersion: "1.0.0")
        XCTAssertEqual(packet[0], 6)
        let json = try XCTUnwrap(JSONSerialization.jsonObject(with: Data(packet.dropFirst())) as? [String: Any])
        XCTAssertEqual(json["model"] as? String, "Watch7,5")
        XCTAssertEqual(json["battery"] as? Int, 80)
        XCTAssertEqual(json["proto"] as? Int, 2)
        XCTAssertLessThan(packet.count, 512)
    }

    func testPongEchoesSequence() {
        XCTAssertEqual(PCNotification.pong(sequence: 7, at: 10), [9, 7, 0, 0, 0, 0x10, 0x27])
    }

    func testPcInfoDecodes() {
        let info = PCInfo.decode(Data(#"{"name":"DESKTOP-1","os":"Windows","proto":2,"app":"1.0.0"}"#.utf8))
        XCTAssertEqual(info?.name, "DESKTOP-1")
        XCTAssertEqual(info?.proto, 2)
    }
}

final class AimTrackerTests: XCTestCase {
    /// Held like a remote: 12 o'clock edge (+y) forward, display (+z) up → gravity (0, 0, -1) in the device frame.
    private func sample(_ t: Double, tilt: Double = 0, yawRate: Double = 0) -> MotionSample {
        MotionSample(timestamp: t, gravity: Vector3(0, -sin(tilt), -cos(tilt)), rotationRate: Vector3(0, 0, yawRate))
    }

    func testFlatIsLevel() {
        let a = AimTracker()
        a.update(sample(0))
        XCTAssertEqual(a.upDegrees, 0, accuracy: 1e-9)
    }

    func testTippingUpRaisesElevationFromGravity() {
        let a = AimTracker()
        a.update(sample(0, tilt: 10 * .pi / 180))
        XCTAssertEqual(a.upDegrees, 10, accuracy: 1e-6)
    }

    func testTurningLeftIntegratesHeading() {
        let a = AimTracker()
        for i in 0...50 { a.update(sample(Double(i) * 0.01, yawRate: .pi / 2)) } // 90°/s for 0.5 s
        XCTAssertEqual(a.leftDegrees, 45, accuracy: 0.01)
    }

    func testRecenterZeroesBothAxes() {
        let a = AimTracker()
        for i in 0...20 { a.update(sample(Double(i) * 0.01, tilt: 0.2, yawRate: 1)) }
        a.recenter()
        XCTAssertEqual(a.leftDegrees, 0, accuracy: 1e-9)
        XCTAssertEqual(a.upDegrees, 0, accuracy: 1e-9)
    }

    func testLongGapsAreClampedSoAStallCantFlingThePointer() {
        let a = AimTracker()
        a.update(sample(0, yawRate: 1))
        a.update(sample(2, yawRate: 1)) // 2 s gap counts as 50 ms
        XCTAssertEqual(a.heading, 0.05, accuracy: 1e-9)
    }

    func testHeadingWrapsToPlusMinus180() {
        let a = AimTracker()
        for i in 0...400 { a.update(sample(Double(i) * 0.01, yawRate: .pi)) } // 720°
        XCTAssertLessThanOrEqual(abs(a.leftDegrees), 180)
    }

    func testDriftCompensationLearnsAResidualBiasWhenStill() {
        let a = AimTracker()
        a.driftCompensation = true
        for i in 0...1500 { a.update(sample(Double(i) * 0.01, yawRate: 0.005)) } // 0.29 °/s residual, hand still
        XCTAssertEqual(a.estimatedBias, 0.005, accuracy: 0.001)
        let before = a.leftDegrees
        for i in 1501...2500 { a.update(sample(Double(i) * 0.01, yawRate: 0.005)) }
        XCTAssertLessThan(abs(a.leftDegrees - before), 0.5) // drift mostly removed
    }
}

final class GestureTests: XCTestCase {
    func testQuickTouchIsATapStampedAtTouchDown() {
        var c = PadGestureClassifier()
        _ = c.touchDown(at: 1.0, x: 50, y: 50)
        XCTAssertEqual(c.tick(at: 1.1), [])
        XCTAssertEqual(c.touchUp(at: 1.15, x: 52, y: 51), [.tap(touchDown: 1.0)])
    }

    func testHoldStartsADragAndReleaseEndsIt() {
        var c = PadGestureClassifier()
        _ = c.touchDown(at: 1.0, x: 50, y: 50)
        XCTAssertEqual(c.tick(at: 1.36), [.holdStart(touchDown: 1.0)])
        XCTAssertTrue(c.isHolding)
        XCTAssertEqual(c.touchMoved(at: 1.5, x: 120, y: 50), []) // thumb movement ignored while dragging
        XCTAssertEqual(c.touchUp(at: 2.0, x: 120, y: 50), [.holdEnd(at: 1.95)])
    }

    func testSwipesInFourDirections() {
        let cases: [(Double, Double, Gesture)] = [(-40, 0, .swipeLeft), (40, 0, .swipeRight), (0, -40, .swipeUp), (0, 40, .swipeDown)]
        for (dx, dy, expected) in cases {
            var c = PadGestureClassifier()
            _ = c.touchDown(at: 0, x: 80, y: 80)
            _ = c.touchMoved(at: 0.05, x: 80 + dx / 2, y: 80 + dy / 2)
            XCTAssertEqual(c.touchUp(at: 0.15, x: 80 + dx, y: 80 + dy), [.swipe(expected, at: 0.15)])
        }
    }

    func testMovingThenStoppingShortIsNeitherClickNorSwipe() {
        var c = PadGestureClassifier()
        _ = c.touchDown(at: 0, x: 80, y: 80)
        _ = c.touchMoved(at: 0.05, x: 95, y: 80)
        XCTAssertEqual(c.tick(at: 0.5), []) // movement cancelled the hold
        XCTAssertEqual(c.touchUp(at: 0.5, x: 98, y: 80), [])
    }

    func testSlowDragAcrossThePadIsNotASwipe() {
        var c = PadGestureClassifier()
        _ = c.touchDown(at: 0, x: 20, y: 80)
        _ = c.touchMoved(at: 0.1, x: 40, y: 80)
        XCTAssertEqual(c.touchUp(at: 1.0, x: 120, y: 80), [])
    }

    func testSwipesCanBeDisabled() {
        var c = PadGestureClassifier()
        c.swipesEnabled = false
        _ = c.touchDown(at: 0, x: 80, y: 80)
        _ = c.touchMoved(at: 0.05, x: 30, y: 80)
        XCTAssertEqual(c.touchUp(at: 0.1, x: 20, y: 80), [])
    }

    func testWristFlickFiresOnceForAShortSpike() {
        var d = WristFlickDetector()
        func s(_ t: Double, _ g: Double) -> MotionSample {
            MotionSample(timestamp: t, gravity: Vector3(0, 0, -1), rotationRate: Vector3(0, 0, 0), userAcceleration: Vector3(g, 0, 0))
        }
        XCTAssertFalse(d.update(s(0.00, 0.2)))
        XCTAssertFalse(d.update(s(0.01, 2.0)))
        XCTAssertFalse(d.update(s(0.05, 2.5)))
        XCTAssertTrue(d.update(s(0.08, 0.3)))
        XCTAssertFalse(d.update(s(0.10, 2.2)))   // within the refractory period
        XCTAssertFalse(d.update(s(0.12, 0.1)))
    }

    func testSustainedAccelerationIsNotAFlick() {
        var d = WristFlickDetector()
        var fired = false
        for i in 0..<50 {
            fired = fired || d.update(MotionSample(timestamp: Double(i) * 0.01, gravity: Vector3(0, 0, -1), rotationRate: Vector3(0, 0, 0), userAcceleration: Vector3(1.8, 0, 0)))
        }
        fired = fired || d.update(MotionSample(timestamp: 0.6, gravity: Vector3(0, 0, -1), rotationRate: Vector3(0, 0, 0), userAcceleration: Vector3(0, 0, 0)))
        XCTAssertFalse(fired)
    }

    func testScrollAccumulatorKeepsFractions() {
        var s = ScrollAccumulator()
        for _ in 0..<13 { s.add(0.25) }          // 3.25 (exact in binary)
        XCTAssertEqual(s.takeWhole(), 3)
        XCTAssertEqual(s.pending, 0.25, accuracy: 1e-9)
        s.add(-2.75)                              // -2.5
        XCTAssertEqual(s.takeWhole(), -2)
        XCTAssertEqual(s.pending, -0.5, accuracy: 1e-9)
    }
}
