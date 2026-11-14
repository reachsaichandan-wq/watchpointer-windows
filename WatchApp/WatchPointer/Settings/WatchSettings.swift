import Foundation

/// Watch-side settings (pointer sensitivity lives on the PC, where it can be tuned against the real screen).
enum WatchSettingKey {
    static let crownSpeed = "crownSpeed"          // multiplier on Crown scroll distance
    static let invertCrown = "invertCrown"
    static let swipes = "swipesEnabled"
    static let shake = "shakeEnabled"             // on: repeated reversals of >1 g; pointing never does that
    static let wristFlick = "wristFlickEnabled"   // off: brisk pointing can approach a flick
    static let twist = "twistEnabled"             // off: rolling the wrist past 35° repeats volume steps
    static let lowerToPause = "lowerToPause"      // on: pointing at the floor pauses the cursor
    static let driftCompensation = "driftCompensation"
    static let haptics = "haptics"
}

enum WatchSettingDefaults {
    static func register() {
        UserDefaults.standard.register(defaults: [
            WatchSettingKey.crownSpeed: 1.0,
            WatchSettingKey.invertCrown: false,
            WatchSettingKey.swipes: true,
            WatchSettingKey.shake: true,
            WatchSettingKey.wristFlick: false,
            WatchSettingKey.twist: false,
            WatchSettingKey.lowerToPause: true,
            WatchSettingKey.driftCompensation: false,
            WatchSettingKey.haptics: true,
        ])
    }
}

/// PC scroll distance, in pixels, per unit of Crown turn (original: 100). Scaled by the crown speed setting.
let crownPixelsPerUnit = 100.0
