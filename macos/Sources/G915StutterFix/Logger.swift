import Foundation

final class Logger {
    enum Level: Int {
        case error = 0
        case warn = 1
        case info = 2
        case trace = 3
    }

    private let level: Level
    private let fileURL: URL?
    private let queue = DispatchQueue(label: "G915StutterFix.logger")
    private let formatter: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return f
    }()

    init(config: FilterConfig) {
        switch config.logLevel.lowercased() {
        case "error": level = .error
        case "warn", "warning": level = .warn
        case "trace", "debug": level = .trace
        default: level = .info
        }

        if config.logFilePath.isEmpty {
            fileURL = FilterConfig.defaultLogURL
        } else {
            fileURL = URL(fileURLWithPath: config.logFilePath)
        }

        if let fileURL {
            try? FileManager.default.createDirectory(
                at: fileURL.deletingLastPathComponent(),
                withIntermediateDirectories: true
            )
        }
    }

    func error(_ message: String) { log(.error, message) }
    func warn(_ message: String) { log(.warn, message) }
    func info(_ message: String) { log(.info, message) }
    func trace(_ message: String) { log(.trace, message) }

    private func log(_ level: Level, _ message: String) {
        guard level.rawValue <= self.level.rawValue else { return }
        let line = "\(formatter.string(from: Date())) [\(String(describing: level).uppercased())] \(message)\n"
        fputs(line, stderr)
        guard let fileURL else { return }
        queue.async {
            guard let data = line.data(using: .utf8) else { return }
            if FileManager.default.fileExists(atPath: fileURL.path) {
                if let handle = try? FileHandle(forWritingTo: fileURL) {
                    defer { try? handle.close() }
                    _ = try? handle.seekToEnd()
                    try? handle.write(contentsOf: data)
                }
            } else {
                try? data.write(to: fileURL)
            }
        }
    }
}
