import AppKit
import CaptureCore
import Combine
import MentraBluetoothSDK

@MainActor
final class GlassesController: NSObject, ObservableObject, MentraBluetoothSDKDelegate {
    private let sdk: MentraBluetoothSDK
    private var scanSession: ScanSession?
    private var epoch = 0
    private var observers: [NSObjectProtocol] = []
    @Published var devices: [Device] = []
    @Published var selectedID: String?
    @Published var status = "Scan and explicitly choose your Mentra Live."
    @Published var operationBusy = false
    @Published private(set) var scanning = false
    @Published private(set) var connected = false
    @Published private(set) var ready = false
    @Published private(set) var captureBusy = false
    @Published private(set) var captureAllowed = false
    @Published private(set) var captureStatus = "No photo requested."
    @Published private(set) var uploadMode: CaptureUploadMode = .wifi
    @Published private(set) var usbTunnelConfigured = false
    @Published private(set) var cameraSetupFailed = false
    private var cameraSetupAttempted = false
    private var galleryDisabled = false
    private var sleeping = false

    private lazy var capture = CaptureSession { [weak self] configuration, requestID in
        guard let self else { throw CancellationError() }
        // HTTP delivery only: the webhook chooses LAN or ADB-reversed loopback.
        // Do not fall back to sending image bytes through the Bluetooth SDK.
        do {
            _ = try await self.sdk.requestPhoto(PhotoRequest(
                requestId: requestID, size: .medium,
                webhookUrl: configuration.webhook.absoluteString, authToken: configuration.token,
                compress: .medium, save: false, sound: true, transferMethod: "direct",
                presendThumbnail: false
            ))
        } catch {
            throw Self.safeCaptureFailure(error)
        }
    }

    private static func safeCaptureFailure(_ error: Error) -> PhotoCaptureFailure {
        guard let sdkError = error as? BluetoothSdkError else {
            if let urlError = error as? URLError {
                return urlError.code == .timedOut ? .timeout : .network
            }
            return .rejected
        }
        let code = sdkError.code.lowercased()
        // Inspect descriptions locally, but never interpolate them into UI/logs.
        let description = sdkError.message.lowercased()
        let pattern = #"(?:http|status(?: code)?|response(?: code)?)[\s:=_-]*([45][0-9]{2})\b"#
        if let regex = try? NSRegularExpression(pattern: pattern),
           let match = regex.firstMatch(
               in: description, range: NSRange(description.startIndex..., in: description)
           ), let range = Range(match.range(at: 1), in: description),
           let status = Int(description[range]) {
            return .receiverHTTP(status)
        }
        if ["unauthorized", "authentication_failed", "invalid_auth_token", "http_401", "http_403"].contains(code) {
            return .receiverHTTP(code == "http_403" ? 403 : 401)
        }
        if code == "request_timeout" || description.contains("timed out") {
            return .timeout
        }
        if ["camera_busy", "busy"].contains(code) { return .cameraBusy }
        if ["not_connected", "glasses_not_connected", "device_disconnected", "disconnected"].contains(code) {
            return .disconnected
        }
        if ["upload_failed", "photo_upload_failed", "network_error", "connection_failed"].contains(code)
            || description.contains("connection refused") || description.contains("unreachable") {
            return .network
        }
        return .rejected
    }

    override init() {
        #if !SAFE_CAPTURE_PRIVATE_DIAGNOSTICS
        fatalError("Use macos/build.sh: SDK credential-safe diagnostic suppression is required.")
        #else
        sdk = MentraBluetoothSDK(configuration: MentraBluetoothSDKConfiguration(analytics: .disabled))
        #endif
        super.init()
        sdk.delegate = self
        capture.onChange = { [weak self] in self?.refreshCaptureUI() }
        let center = NSWorkspace.shared.notificationCenter
        observers.append(center.addObserver(forName: NSWorkspace.willSleepNotification,
                                           object: nil, queue: .main) { [weak self] _ in
            Task { @MainActor in self?.sleep() }
        })
        observers.append(center.addObserver(forName: NSWorkspace.didWakeNotification,
                                           object: nil, queue: .main) { [weak self] _ in
            Task { @MainActor in
                guard let self else { return }
                self.sleeping = false
                self.synchronize(self.sdk.glasses)
                self.status = "Awake; explicitly reconnect. Capture has not resumed."
            }
        })
        synchronize(sdk.glasses)
    }

    private func refreshCaptureUI() {
        if !capture.readiness.galleryDisabled { galleryDisabled = false }
        if uploadMode == .usbADB && !capture.readiness.usbTunnelConfigured { usbTunnelConfigured = false }
        captureBusy = capture.busy
        cameraSetupFailed = cameraSetupAttempted && !galleryDisabled && !operationBusy
            && connected && ready && !captureBusy
        captureAllowed = capture.canCapture && !operationBusy
        captureStatus = (capture.readiness.reason.map { "\($0)\n" } ?? "") + capture.message
        // A previous photo can finish after the glasses reconnect. Retry the
        // new session's first setup when that request releases the busy gate.
        prepareCameraIfNeeded()
    }

    private func synchronize(_ glasses: GlassesRuntimeState) {
        connected = glasses.connected
        ready = glasses.ready
        if !connected || !ready {
            galleryDisabled = false
            cameraSetupAttempted = false
            usbTunnelConfigured = false
            epoch += 1
            capture.stop(reason: connected ? "Glasses unready" : "Disconnected")
        }
        var state = CaptureReadiness()
        state.connected = connected
        state.ready = ready
        state.galleryDisabled = galleryDisabled
        state.wifiConnected = glasses.wifi?.isConnected == true
        state.uploadMode = uploadMode
        state.usbTunnelConfigured = usbTunnelConfigured
        state.sleeping = sleeping
        capture.readiness = state
        refreshCaptureUI()
    }

    func scan() {
        guard !connected, !captureBusy, !operationBusy else { return }
        devices = []
        selectedID = nil
        status = "Scanning for 10 seconds… Choose a device; never auto-connect."
        do {
            scanSession?.stop()
            scanSession = try sdk.scan(model: .mentraLive, timeout: 10) { [weak self] devices in
                self?.devices = devices
            }
            scanning = true
        } catch {
            status = "Scan failed. Enable Bluetooth and allow this app in System Settings > Privacy & Security > Bluetooth."
        }
    }

    func setUploadMode(_ mode: CaptureUploadMode) {
        guard !captureBusy, !operationBusy, mode != uploadMode else { return }
        capture.stop(reason: "Upload transport changed")
        uploadMode = mode
        usbTunnelConfigured = false
        synchronize(sdk.glasses)
        status = mode == .usbADB
            ? "USB uploads selected. Bluetooth still controls capture. Configure the ADB tunnel in Terminal."
            : "Wi-Fi uploads selected. Glasses and Mac must share a reachable trusted LAN."
    }

    func confirmUSBTunnel(_ confirmed: Bool) {
        guard uploadMode == .usbADB, !captureBusy, !operationBusy else { return }
        usbTunnelConfigured = confirmed
        synchronize(sdk.glasses)
    }

    func connect() {
        guard let device = devices.first(where: { $0.id == selectedID }),
              !captureBusy, !operationBusy, !connected else { return }
        scanSession?.stop()
        sdk.stopScan()
        do {
            epoch += 1
            galleryDisabled = false
            cameraSetupAttempted = false
            try sdk.connect(to: device, options: ConnectOptions(saveAsDefault: false, requiresAncs: false))
            status = "Connecting to the selected device. Accept the Bluetooth pairing prompt if shown."
        } catch {
            status = "Connection failed. Retry pairing; check Bluetooth and that another app is not holding the glasses."
        }
    }

    func disconnect() {
        epoch += 1
        capture.stop(reason: "Disconnected")
        galleryDisabled = false
        cameraSetupAttempted = false
        usbTunnelConfigured = false
        scanSession?.stop()
        sdk.stopScan()
        sdk.cancelConnectionAttempt()
        sdk.disconnect()
        synchronize(sdk.glasses)
        status = "Disconnected. Any already-issued capture cannot be retracted."
    }

    func retryCameraSetup() {
        guard connected, ready, !sleeping, !operationBusy, !captureBusy,
              !galleryDisabled else { return }
        cameraSetupAttempted = false
        prepareCameraIfNeeded()
    }

    private func prepareCameraIfNeeded() {
        guard connected, ready, !sleeping, !cameraSetupAttempted,
              !operationBusy, !captureBusy else { return }
        cameraSetupAttempted = true
        operationBusy = true
        galleryDisabled = false
        cameraSetupFailed = false
        captureAllowed = false
        let startedEpoch = epoch
        status = "Preparing camera privacy settings. Firmware-version validation is skipped."
        Task {
            defer {
                operationBusy = false
                synchronize(sdk.glasses)
            }
            do {
                let acknowledgement = try await sdk.setGalleryModeEnabled(false)
                guard startedEpoch == epoch, sdk.glasses.connected, sdk.glasses.ready else { return }
                guard CameraSetupAcknowledgement.confirmsGalleryDisabled(
                    setting: acknowledgement.setting,
                    status: acknowledgement.values["status"] as? String,
                    enabled: acknowledgement.enabled,
                    errorCode: acknowledgement.errorCode
                ) else {
                    status = "Camera privacy settings were not confirmed. Click Retry camera setup; no photo was requested."
                    return
                }
                galleryDisabled = true
                status = uploadMode == .usbADB
                    ? "Camera ready. Configure and confirm your USB tunnel, then choose Single photo. Firmware matching is not checked."
                    : "Camera ready. Connect glasses Wi-Fi, then choose Single photo. Firmware matching is not checked."
            } catch {
                guard startedEpoch == epoch else { return }
                status = "Camera setup failed. Check the glasses connection, then click Retry camera setup. No firmware was changed."
            }
        }
    }

    func provisionWiFi(ssid: String, password: String) {
        guard connected, ready, !operationBusy, !captureBusy, !ssid.isEmpty,
              ssid.utf8.count <= 32, password.utf8.count <= 63 else {
            status = "Wi-Fi setup needs ready glasses, SSID (1–32 bytes) and password (0–63 bytes)."
            return
        }
        operationBusy = true
        refreshCaptureUI()
        let startedEpoch = epoch
        status = "Joining Wi-Fi… Credentials are transient and never stored by this app."
        Task {
            defer { operationBusy = false; refreshCaptureUI() }
            do {
                _ = try await sdk.sendWifiCredentials(ssid: ssid, password: password)
                guard startedEpoch == epoch else { return }
                status = "Glasses joined Wi-Fi. Ensure this Mac is reachable on the same trusted LAN."
            } catch {
                status = "Wi-Fi setup failed. Check the password and network, move closer and retry."
            }
            synchronize(sdk.glasses)
        }
    }

    func takePhoto(webhook: String, token: String, repeatSnapshots: Bool) {
        guard !operationBusy else { return }
        do {
            let configuration = try CaptureConfiguration(webhook: webhook, token: token, uploadMode: uploadMode)
            if !capture.begin(configuration, repeating: repeatSnapshots) {
                status = "Capture blocked: \(capture.readiness.reason ?? "wait for the in-flight request to finish")."
            }
        } catch {
            status = (error as? ConfigurationError)?.errorDescription ?? "Invalid capture configuration."
        }
    }

    func stop() { capture.stop() }

    private func sleep() {
        sleeping = true
        capture.stop(reason: "Mac sleeping")
        disconnect()
        synchronize(sdk.glasses)
        status = "Mac sleeping; capture stopped. Reconnect after wake; no automatic resume."
    }

    func shutdown() {
        capture.stop(reason: "App quitting")
        disconnect()
        for observer in observers { NSWorkspace.shared.notificationCenter.removeObserver(observer) }
        observers.removeAll()
        sdk.delegate = nil
        sdk.invalidate()
    }

    func mentraBluetoothSDK(_ sdk: MentraBluetoothSDK, didUpdateGlasses glasses: GlassesRuntimeState) {
        synchronize(glasses)
    }

    func mentraBluetoothSDK(_ sdk: MentraBluetoothSDK, didUpdateScan scan: BluetoothScanState) {
        devices = scan.devices
        scanning = scan.active
    }

    func mentraBluetoothSDK(_ sdk: MentraBluetoothSDK, didStopScan reason: ScanStopReason) {
        scanning = false
        status = devices.isEmpty
            ? "No glasses found. Enable Bluetooth, open the glasses pairing window (triple-press power), and retry."
            : "Choose your glasses from the list, then Connect."
    }

    func mentraBluetoothSDK(_ sdk: MentraBluetoothSDK, didFail error: BluetoothSdkError) {
        // Never interpolate SDK messages or status snapshots: they may contain credentials.
        status = "Bluetooth operation failed. Check permissions/pairing and retry."
    }
}
