import CaptureCore
import Foundation

/// Read only our private, sandbox-owned file. Never execute or log its contents.
struct LocalConfiguration {
    static let maximumBytes = 16 * 1024
    static var fileURL: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("SafeCaptureMentra", isDirectory: true)
            .appendingPathComponent(".env")
    }

    let wifiWebhook: String
    let usbWebhook: String
    let token: String
    let ssid: String
    let password: String
    let adbSerial: String
    let uploadMode: CaptureUploadMode

    static func load() throws -> LocalConfiguration {
        let handle = try FileHandle(forReadingFrom: fileURL)
        defer { try? handle.close() }
        let data = try handle.read(upToCount: maximumBytes + 1) ?? Data()
        guard data.count <= maximumBytes, let text = String(data: data, encoding: .utf8),
              !text.contains("\0") else { throw LoadError.invalid }
        let values = try parse(text)
        let wifiWebhook = values["SAFE_CAPTURE_WEBHOOK_URL"] ?? ""
        let usbWebhook = values["SAFE_CAPTURE_USB_WEBHOOK_URL"] ?? "http://127.0.0.1:8091/mentra/photo"
        let token = values["SAFE_CAPTURE_INGEST_TOKEN"] ?? ""
        let ssid = values["SAFE_CAPTURE_WIFI_SSID"] ?? ""
        let password = values["SAFE_CAPTURE_WIFI_PASSWORD"] ?? ""
        let adbSerial = values["SAFE_CAPTURE_ADB_SERIAL"] ?? ""
        guard let mode = CaptureUploadMode(rawValue: values["SAFE_CAPTURE_UPLOAD_MODE"] ?? "wifi"),
              ssid.utf8.count <= 32, password.utf8.count <= 63 else { throw LoadError.invalid }
        if !wifiWebhook.isEmpty {
            _ = try CaptureConfiguration.validateWebhook(wifiWebhook, uploadMode: .wifi)
        }
        _ = try CaptureConfiguration.validateWebhook(usbWebhook, uploadMode: .usbADB)
        if !token.isEmpty {
            // Reuse token validation without requiring Wi-Fi configuration.
            _ = try CaptureConfiguration(webhook: usbWebhook, token: token, uploadMode: .usbADB)
        }
        return LocalConfiguration(
            wifiWebhook: wifiWebhook, usbWebhook: usbWebhook, token: token,
            ssid: ssid, password: password, adbSerial: adbSerial, uploadMode: mode
        )
    }

    private static func parse(_ text: String) throws -> [String: String] {
        let keys: Set<String> = [
            "SAFE_CAPTURE_WEBHOOK_URL", "SAFE_CAPTURE_USB_WEBHOOK_URL",
            "SAFE_CAPTURE_INGEST_TOKEN", "SAFE_CAPTURE_WIFI_SSID",
            "SAFE_CAPTURE_WIFI_PASSWORD", "SAFE_CAPTURE_ADB_SERIAL",
            "SAFE_CAPTURE_UPLOAD_MODE",
        ]
        var values: [String: String] = [:]
        for rawLine in text.components(separatedBy: .newlines) {
            var line = rawLine.trimmingCharacters(in: .whitespaces)
            if line.isEmpty || line.hasPrefix("#") { continue }
            if line.hasPrefix("export ") { line = String(line.dropFirst(7)) }
            guard let separator = line.firstIndex(of: "=") else { throw LoadError.invalid }
            let key = line[..<separator].trimmingCharacters(in: .whitespaces)
            guard keys.contains(key) else { continue }
            var value = line[line.index(after: separator)...].trimmingCharacters(in: .whitespaces)
            if value.first == "\"" || value.first == "'" {
                guard value.count >= 2, value.last == value.first else { throw LoadError.invalid }
                let doubleQuoted = value.first == "\""
                value = String(value.dropFirst().dropLast())
                if doubleQuoted {
                    value = value.replacingOccurrences(of: "\\\"", with: "\"")
                        .replacingOccurrences(of: "\\\\", with: "\\")
                }
            } else if let comment = value.firstIndex(of: "#") {
                value = value[..<comment].trimmingCharacters(in: .whitespaces)
            }
            values[key] = value
        }
        return values
    }

    private enum LoadError: Error { case invalid }
}
