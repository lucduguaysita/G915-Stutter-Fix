// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "G915StutterFix",
    platforms: [.macOS(.v13)],
    targets: [
        .executableTarget(
            name: "G915StutterFix",
            path: "Sources/G915StutterFix"
        )
    ]
)
