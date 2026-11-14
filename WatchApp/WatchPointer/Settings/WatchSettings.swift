import Foundation

/// Watch-side settings (pointer sensitivity lives on the PC, where it can be tuned against the real screen).
enum WatchSettingKey {
    static let crownSpeed = "crownSpeed"          // multiplier on Crown scroll distance
    static let invertCrown = "invertCrown"
    static let swipes = "swipesEnabled"
    static let wristFlick = "wristFlickEnabled"   // off by default: ordinary pointing can come close to a flick
    static let driftCompensation = "driftCompensation"
    static let haptics = "haptics"
}

enum WatchSettingDefaults {
    static func register() {
        UserDefaults.standard.register(defaults: [
            WatchSettingKey.crownSpeed: 1.0,
            WatchSettingKey.invertCrown: false,
            WatchSettingKey.swipes: true,
            WatchSettingKey.wristFlick: false,
            WatchSettingKey.driftCompensation: false,
            WatchSettingKey.haptics: true,
        ])
    }
}

/// PC scroll distance, in pixels, per unit of Crown turn (original: 100). Scaled by the crown speed setting.
let crownPixelsPerUnit = 100.0
