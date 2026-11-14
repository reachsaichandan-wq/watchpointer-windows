import Foundation

/// GATT identifiers. Service, input and output are the original WatchPointer UUIDs, so this watch app also works
/// with the original macOS receiver (v1 features only), and the Windows receiver works with the original watch app.
/// See Shared/Protocol/PROTOCOL.md.
public enum WatchPointerUUID {
    public static let service = "6B1E0001-9F4C-4C2A-9B8E-3D2A1C0F5E01"
    /// Watch → PC writes.
    public static let input = "6B1E0002-9F4C-4C2A-9B8E-3D2A1C0F5E01"
    /// PC → watch notifications. Subscribing tells the PC the watch connected.
    public static let output = "6B1E0003-9F4C-4C2A-9B8E-3D2A1C0F5E01"
    /// v2: readable JSON describing the PC.
    public static let info = "6B1E0004-9F4C-4C2A-9B8E-3D2A1C0F5E01"
}

public let protocolVersion = 2

/// First byte of every packet. 0–7 are v1 (original), 8–9 are v2.
public enum PacketKind: UInt8 {
    case aim = 0, leftClick = 1, rightClick = 2, scroll = 3, center = 4, pause = 5, info = 6, drag = 7
    case gesture = 8, pong = 9
}

/// v2 gesture ids carried in `.gesture` packets (`a`). The PC maps them to actions per profile.
public enum Gesture: Int16, CaseIterable {
    case swipeLeft = 1, swipeRight = 2, swipeUp = 3, swipeDown = 4
    case handDoubleTap = 5
    case wristFlick = 6
    case buttonA = 16, buttonB = 17, buttonC = 18, buttonD = 19
}

/// Builds packets: [kind, Int16 LE a, Int16 LE b] plus optional [UInt16 LE ms], or [6] + JSON for info.
public enum PacketEncoder {
    /// Hundredths of a degree, rounded to nearest (ties away from zero) and clamped to Int16, like the original app.
    public static func centidegrees(_ degrees: Double) -> Int16 {
        Int16(clamping: Int((degrees * 100).rounded()))
    }

    /// The watch's clock in milliseconds, truncated to 16 bits (wraps every 65.536 s). The PC unwraps it.
    public static func watchMs(_ seconds: TimeInterval) -> UInt16 {
        UInt16(truncatingIfNeeded: Int64((seconds * 1000).rounded(.down)))
    }

    public static func packet(_ kind: PacketKind, _ a: Int16 = 0, _ b: Int16 = 0, ms: UInt16? = nil) -> [UInt8] {
        var bytes: [UInt8] = [kind.rawValue,
                              UInt8(truncatingIfNeeded: a), UInt8(truncatingIfNeeded: a >> 8),
                              UInt8(truncatingIfNeeded: b), UInt8(truncatingIfNeeded: b >> 8)]
        if let ms {
            bytes += [UInt8(truncatingIfNeeded: ms), UInt8(truncatingIfNeeded: ms >> 8)]
        }
        return bytes
    }

    /// `left`: degrees turned left since Center; `up`: degrees tipped up since Center; `at`: watch time (s).
    public static func aim(leftDegrees: Double, upDegrees: Double, at time: TimeInterval) -> [UInt8] {
        packet(.aim, centidegrees(leftDegrees), centidegrees(upDegrees), ms: watchMs(time))
    }

    public static func info(model: String, os: String, battery: Int, charging: Bool, name: String, appVersion: String) -> [UInt8] {
        let object: [String: Any] = [
            "model": model,
            "os": os,
            "battery": battery,
            "charging": charging,
            "name": String(name.prefix(40)), // keep the packet small
            "proto": protocolVersion,
            "app": appVersion,
        ]
        let json = (try? JSONSerialization.data(withJSONObject: object, options: [.sortedKeys])) ?? Data("{}".utf8)
        return [PacketKind.info.rawValue] + [UInt8](json)
    }
}

/// v2 PC → watch notifications on the output characteristic.
public enum PCNotification: Equatable {
    case ping(sequence: UInt16)
    case state(paused: Bool, laser: Bool, profile: String)

    public static func decode(_ bytes: [UInt8]) -> PCNotification? {
        guard bytes.count >= 2 else { return nil }
        switch bytes[0] {
        case 1 where bytes.count >= 3:
            return .ping(sequence: UInt16(bytes[1]) | UInt16(bytes[2]) << 8)
        case 2:
            let profile = String(decoding: bytes[2...], as: UTF8.self)
            return .state(paused: bytes[1] & 1 != 0, laser: bytes[1] & 2 != 0, profile: profile)
        default:
            return nil
        }
    }

    /// Reply to a ping: [9, seq LE, 0, 0, ms LE].
    public static func pong(sequence: UInt16, at time: TimeInterval) -> [UInt8] {
        PacketEncoder.packet(.pong, Int16(bitPattern: sequence), 0, ms: PacketEncoder.watchMs(time))
    }
}

/// What the PC serves on the info characteristic.
public struct PCInfo: Decodable, Equatable {
    public let name: String
    public let os: String?
    public let proto: Int?
    public let app: String?

    public static func decode(_ data: Data) -> PCInfo? { try? JSONDecoder().decode(PCInfo.self, from: data) }
}
