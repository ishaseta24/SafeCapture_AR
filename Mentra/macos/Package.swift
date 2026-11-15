// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "SafeCaptureMentra",
    platforms: [.macOS(.v13)],
    products: [.executable(name: "SafeCaptureMentra", targets: ["SafeCaptureMentra"])],
    dependencies: [
        .package(url: "https://github.com/Mentra-Community/mentra-bluetooth-sdk-ios.git", exact: "3.3.0"),
    ],
    targets: [
        .target(name: "CaptureCore"),
        .executableTarget(
            name: "SafeCaptureMentra",
            dependencies: [
                "CaptureCore",
                .product(name: "MentraBluetoothSDK", package: "mentra-bluetooth-sdk-ios"),
            ]
        ),
    ]
)
