import Foundation

public enum CaptureCoreVersion {
    public static let sdkRelease = "3.3.0"
}

public enum CameraSetupAcknowledgement {
    /// The SDK correlates this acknowledgement to our gallery-disable request.
    /// Do not accept its default "applied" fallback when status is missing.
    public static func confirmsGalleryDisabled(
        setting: String, status: String?, enabled: Bool?, errorCode: String?
    ) -> Bool {
        setting == "gallery_mode" && status?.lowercased() == "applied"
            && enabled != true && (errorCode?.isEmpty ?? true)
    }
}

/// Only fixed messages and numeric HTTP statuses may reach the UI.
/// Upstream error descriptions can contain credentials and must not be shown.
public enum PhotoCaptureFailure: Error {
    case timeout, network, cameraBusy, disconnected, rejected
    case receiverHTTP(Int)

    public var recoveryMessage: String {
        switch self {
        case .timeout:
            return "Photo request timed out without delivery confirmation. An issued upload may still finish. Check glasses Wi-Fi, receiver address and Mac firewall."
        case .network:
            return "Photo upload failed. Check that the glasses can reach the Mac receiver; a hotspot or firewall may block connections between devices."
        case .cameraBusy:
            return "The glasses camera is busy. Wait for the previous photo to finish."
        case .disconnected:
            return "The glasses disconnected during capture. Reconnect before retrying."
        case .receiverHTTP(let status) where status == 401 || status == 403:
            return "Receiver rejected authentication (HTTP \(status)). Enter the exact receiver token; restart the receiver if its configured token changed."
        case .receiverHTTP(let status):
            return "Receiver rejected the photo upload (HTTP \(status)). Check the receiver's upload format, limits and availability."
        case .rejected:
            return "The glasses rejected the photo request. Check camera readiness and the receiver connection."
        }
    }
}

public enum CaptureUploadMode: String, CaseIterable, Identifiable {
    case wifi
    case usbADB

    public var id: String { rawValue }
    public var title: String {
        switch self {
        case .wifi: return "Wi-Fi"
        case .usbADB: return "USB (ADB reverse)"
        }
    }
}

public struct CaptureConfiguration: Equatable {
    public let webhook: URL
    public let token: String
    public let uploadMode: CaptureUploadMode

    public init(webhook: String, token: String, uploadMode: CaptureUploadMode = .wifi) throws {
        let url = try Self.validateWebhook(webhook, uploadMode: uploadMode)
        guard (32...256).contains(token.utf8.count),
              token.utf8.allSatisfy({ $0 >= 0x21 && $0 <= 0x7e }),
              token != "replace-with-a-random-local-token"
        else { throw ConfigurationError.invalidToken }
        self.webhook = url
        self.token = token
        self.uploadMode = uploadMode
    }

    public static func validateWebhook(_ webhook: String, uploadMode: CaptureUploadMode) throws -> URL {
        let invalid: ConfigurationError = uploadMode == .usbADB ? .invalidUSBWebhook : .invalidWebhook
        guard let url = URL(string: webhook),
              let host = url.host?.lowercased(),
              ["http", "https"].contains(url.scheme?.lowercased() ?? ""),
              url.user == nil, url.password == nil, url.query == nil, url.fragment == nil,
              url.path == "/mentra/photo",
              (1...65535).contains(url.port ?? 8091)
        else { throw invalid }
        switch uploadMode {
        case .wifi:
            guard Self.isLANHost(host) else { throw invalid }
        case .usbADB:
            // This address is the GLASSES' loopback, forwarded by adbd to the
            // Mac. Never accept a LAN/public URL or an implicit port in USB mode.
            guard url.scheme == "http", host == "127.0.0.1",
                  let port = url.port, (1024...65535).contains(port)
            else { throw invalid }
        }
        return url
    }

    private static func isLANHost(_ host: String) -> Bool {
        if host.hasSuffix(".local"), host.count > 6 { return true }
        let parts = host.split(separator: ".", omittingEmptySubsequences: false)
        guard parts.count == 4 else { return false }
        let octets = parts.compactMap { Int($0) }
        guard octets.count == 4, octets.allSatisfy({ (0...255).contains($0) }) else { return false }
        return octets[0] == 10 || (octets[0] == 192 && octets[1] == 168)
            || (octets[0] == 172 && (16...31).contains(octets[1]))
    }
}

public enum ConfigurationError: Error, LocalizedError {
    case invalidWebhook, invalidUSBWebhook, invalidToken
    public var errorDescription: String? {
        switch self {
        case .invalidWebhook:
            return "Use a private LAN IPv4 or .local URL ending /mentra/photo, without credentials or query parameters."
        case .invalidUSBWebhook:
            return "USB mode requires http://127.0.0.1:PORT/mentra/photo (port 1024–65535), without credentials or query parameters."
        case .invalidToken:
            return "Enter the receiver's random 32–256 character token (no spaces)."
        }
    }
}

public struct CaptureReadiness: Equatable {
    public var connected = false
    public var ready = false
    public var galleryDisabled = false
    public var wifiConnected = false
    public var uploadMode: CaptureUploadMode = .wifi
    public var usbTunnelConfigured = false
    public var sleeping = false
    public init() {}

    public var reason: String? {
        if sleeping { return "Mac asleep; reconnect and validate after waking." }
        if !connected { return "Scan, choose your glasses, and connect." }
        if !ready { return "Waiting for glasses to finish booting." }
        if !galleryDisabled { return "Camera setup is not confirmed. Wait for setup or click Retry camera setup." }
        switch uploadMode {
        case .wifi:
            if !wifiConnected { return "Provision glasses Wi-Fi on this trusted LAN." }
        case .usbADB:
            if !usbTunnelConfigured {
                return "Run the USB setup command for these glasses, then confirm the tunnel below."
            }
        }
        return nil
    }
}

/// SDK-independent serial lifecycle. Stop never cancels the underlying delivery await.
@MainActor
public final class CaptureSession {
    public typealias Capture = @MainActor (CaptureConfiguration, String) async throws -> Void
    public var onChange: (() -> Void)?
    public var readiness = CaptureReadiness() {
        didSet {
            if readiness.reason != nil {
                repeating = false
                generation += 1
            }
            onChange?()
        }
    }
    public private(set) var busy = false
    public private(set) var inFlight = false
    public private(set) var repeating = false
    public private(set) var message = "No photo requested."
    private var worker: Task<Void, Never>?
    private var generation = 0
    private let capture: Capture
    private let pause: @MainActor (UInt64) async -> Void

    public init(capture: @escaping Capture,
                pause: @escaping @MainActor (UInt64) async -> Void = { nanoseconds in
                    try? await Task.sleep(nanoseconds: nanoseconds)
                }) {
        self.capture = capture
        self.pause = pause
    }

    public var canCapture: Bool { !busy && readiness.reason == nil }

    @discardableResult
    public func begin(_ configuration: CaptureConfiguration, repeating: Bool, intervalSeconds: UInt64 = 3) -> Bool {
        guard canCapture, configuration.uploadMode == readiness.uploadMode else { return false }
        generation += 1
        let startedGeneration = generation
        self.repeating = repeating
        busy = true // Synchronous admission prevents double-click races.
        onChange?()
        worker = Task { [self] in
            defer {
                busy = false
                inFlight = false
                self.repeating = false
                worker = nil
                onChange?()
            }
            repeat {
                guard generation == startedGeneration, readiness.reason == nil else { break }
                inFlight = true
                message = "Capturing/uploading; Stop cannot retract this request."
                onChange?()
                do {
                    try await capture(configuration, UUID().uuidString)
                    inFlight = false
                    message = "Photo delivered and processed."
                } catch {
                    inFlight = false
                    self.repeating = false
                    // An upload error does not undo the camera-settings ACK.
                    // Stop serial requests, but allow a deliberate manual retry.
                    if configuration.uploadMode == .usbADB {
                        var blocked = readiness
                        blocked.usbTunnelConfigured = false
                        readiness = blocked
                    }
                    message = (error as? PhotoCaptureFailure)?.recoveryMessage
                        ?? "Photo capture or delivery failed. Check the glasses connection, receiver address and token."
                    message += " Serial snapshots stopped; no automatic retry."
                }
                onChange?()
                guard self.repeating, readiness.reason == nil else { break }
                await pause(max(1, min(intervalSeconds, 60)) * 1_000_000_000)
            } while self.repeating
        }
        return true
    }

    public func stop(reason: String = "Stopped") {
        generation += 1
        repeating = false
        message = inFlight
            ? "\(reason). One issued request is still awaiting terminal delivery; it cannot be retracted."
            : "\(reason). No automatic resume."
        // Deliberately do NOT cancel worker or clear busy until the await actually returns.
        onChange?()
    }
}
