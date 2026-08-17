import Foundation

struct FilterConfig: Codable {
    var logLevel: String = "Info"
    var logFilePath: String = ""
    var filterMode: String = "BlockRepress"
    // Default above typical G915 Windows chatter (~28ms). Some units bounce
    // slower on macOS HID paths (~60ms+); 70ms covers that with headroom.
    var minRepeatIntervalMs: Double = 70.0
    var burstBypass: Bool = false
    var filterMouseButtons: Bool = false
    var mouseMinRepeatIntervalMs: Double = 50.0
    var excludedKeys: [String] = ["Back", "Return", "Volume_Down", "Volume_Up"]
    var perKeyMinRepeatIntervalMs: [String: Double] = [:]
    var launchAtLogin: Bool = false

    enum CodingKeys: String, CodingKey {
        case logLevel = "LogLevel"
        case logFilePath = "LogFilePath"
        case filterMode = "FilterMode"
        case minRepeatIntervalMs = "MinRepeatIntervalMs"
        case burstBypass = "BurstBypass"
        case filterMouseButtons = "FilterMouseButtons"
        case mouseMinRepeatIntervalMs = "MouseMinRepeatIntervalMs"
        case excludedKeys = "ExcludedKeys"
        case perKeyMinRepeatIntervalMs = "PerKeyMinRepeatIntervalMs"
        case launchAtLogin = "LaunchAtLogin"
    }

    var isBlockRelease: Bool {
        filterMode.compare("BlockRelease", options: .caseInsensitive) == .orderedSame
    }

    static var defaultConfigURL: URL {
        let home = FileManager.default.homeDirectoryForCurrentUser
        return home
            .appendingPathComponent("Library")
            .appendingPathComponent("Application Support")
            .appendingPathComponent("G915StutterFix")
            .appendingPathComponent("config.json")
    }

    static func load(from url: URL = defaultConfigURL) -> FilterConfig {
        guard FileManager.default.fileExists(atPath: url.path) else {
            var config = FilterConfig()
            config.logFilePath = defaultLogURL.path
            try? config.save(to: url)
            return config
        }

        do {
            let data = try Data(contentsOf: url)
            var config = try JSONDecoder().decode(FilterConfig.self, from: data)
            if config.logFilePath.isEmpty {
                config.logFilePath = defaultLogURL.path
            }
            return config
        } catch {
            var config = FilterConfig()
            config.logFilePath = defaultLogURL.path
            return config
        }
    }

    func save(to url: URL = defaultConfigURL) throws {
        let dir = url.deletingLastPathComponent()
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        let data = try encoder.encode(self)
        try data.write(to: url, options: .atomic)
    }

    static var defaultLogURL: URL {
        FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library")
            .appendingPathComponent("Logs")
            .appendingPathComponent("G915StutterFix.log")
    }
}
