import AppKit
import Foundation

/// Registers a per-user LaunchAgent so the menu-bar filter starts at login.
/// Used instead of SMAppService alone: ad-hoc builds outside /Applications
/// often fail SMAppService registration.
enum LaunchAtLogin {
    static let label = "com.g915stutterfix.macos"

    static var plistURL: URL {
        FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/LaunchAgents")
            .appendingPathComponent("\(label).plist")
    }

    /// Prefer the .app bundle executable when running from a bundle.
    static var launchExecutableURL: URL {
        let bundle = Bundle.main
        if bundle.bundleURL.pathExtension == "app",
           let exe = bundle.executableURL {
            return exe
        }
        return URL(fileURLWithPath: CommandLine.arguments[0]).resolvingSymlinksInPath()
    }

    static var isEnabled: Bool {
        FileManager.default.fileExists(atPath: plistURL.path)
    }

    @discardableResult
    static func setEnabled(_ enabled: Bool) throws -> Bool {
        if enabled {
            try installPlist()
            _ = try? runLaunchctl(["bootout", "gui/\(getuid())", label])
            try runLaunchctl(["bootstrap", "gui/\(getuid())", plistURL.path])
            return true
        } else {
            _ = try? runLaunchctl(["bootout", "gui/\(getuid())", label])
            if FileManager.default.fileExists(atPath: plistURL.path) {
                try FileManager.default.removeItem(at: plistURL)
            }
            return false
        }
    }

    private static func installPlist() throws {
        let exe = launchExecutableURL.path
        let dir = plistURL.deletingLastPathComponent()
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)

        let plist: [String: Any] = [
            "Label": label,
            "ProgramArguments": [exe],
            "RunAtLoad": true,
            "KeepAlive": false,
            "ProcessType": "Interactive",
            "ThrottleInterval": 3,
        ]
        let data = try PropertyListSerialization.data(
            fromPropertyList: plist,
            format: .xml,
            options: 0
        )
        try data.write(to: plistURL, options: .atomic)
    }

    @discardableResult
    private static func runLaunchctl(_ args: [String]) throws -> Int32 {
        let proc = Process()
        proc.executableURL = URL(fileURLWithPath: "/bin/launchctl")
        proc.arguments = args
        let err = Pipe()
        proc.standardError = err
        proc.standardOutput = Pipe()
        try proc.run()
        proc.waitUntilExit()
        // bootout returns non-zero if not loaded; callers ignore that.
        if proc.terminationStatus != 0 && args.first != "bootout" {
            let msg = String(data: err.fileHandleForReading.readDataToEndOfFile(), encoding: .utf8) ?? ""
            throw NSError(
                domain: "LaunchAtLogin",
                code: Int(proc.terminationStatus),
                userInfo: [NSLocalizedDescriptionKey: "launchctl \(args.joined(separator: " ")) failed: \(msg)"]
            )
        }
        return proc.terminationStatus
    }
}
