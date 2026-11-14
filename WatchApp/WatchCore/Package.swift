// swift-tools-version:5.9
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
    ]
)
