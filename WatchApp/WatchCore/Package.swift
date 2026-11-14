// swift-tools-version:6.0
// Platform-independent watch logic (protocol encoding, aim math, gesture classification).
// Foundation only, so `swift test` runs on macOS *and* Linux CI runners without Xcode or a watch.
import PackageDescription

let package = Package(
    name: "WatchCore",
    platforms: [.watchOS(.v11), .macOS(.v14), .iOS(.v17)],
    products: [.library(name: "WatchCore", targets: ["WatchCore"])],
    targets: [
        .target(name: "WatchCore"),
        .testTarget(name: "WatchCoreTests", dependencies: ["WatchCore"]),
    ],
    // Same language mode as the app target (SWIFT_VERSION 5.0 in project.yml).
    swiftLanguageModes: [.v5]
)
