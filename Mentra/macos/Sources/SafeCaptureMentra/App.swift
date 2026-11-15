import AppKit
import CaptureCore
import SwiftUI

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    weak var controller: GlassesController?

    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        guard controller?.captureBusy == true else { return .terminateNow }
        controller?.stop()
        let alert = NSAlert()
        alert.messageText = "An issued photo request may still finish."
        alert.informativeText = "Stop cannot retract a capture/upload already sent to the glasses. Keep this app open to receive its terminal result."
        alert.addButton(withTitle: "Keep open")
        alert.addButton(withTitle: "Quit anyway")
        return alert.runModal() == .alertFirstButtonReturn ? .terminateCancel : .terminateNow
    }

    func applicationWillTerminate(_ notification: Notification) { controller?.shutdown() }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }
}

@main
@MainActor
struct SafeCaptureMentraApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var delegate
    @StateObject private var controller = GlassesController()

    var body: some Scene {
        WindowGroup("SafeCapture · Mentra Live") {
            ControllerView(controller: controller)
                .onAppear { delegate.controller = controller }
        }
        .defaultSize(width: 780, height: 760)
    }
}

@MainActor
private struct ControllerView: View {
    @ObservedObject var controller: GlassesController
    @State private var ssid = ""
    @State private var password = ""
    @State private var wifiWebhook = ""
    @State private var usbWebhook = "http://127.0.0.1:8091/mentra/photo"
    @State private var adbSerial = ""
    @State private var token = ""
    @State private var configurationStatus = ""

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                Text("Mentra Live → this Mac").font(.largeTitle.bold())
                Text("Bluetooth control · Wi-Fi or USB/ADB JPEG uploads · No iPhone")
                Text("Snapshots, not live video. No raw preview. Camera indicators and shutter sound stay on.")
                    .foregroundStyle(.secondary)
                GroupBox("Local .env configuration") {
                    VStack(alignment: .leading, spacing: 8) {
                        HStack {
                            Button("Reload .env") { reloadConfiguration() }
                                .disabled(controller.captureBusy || controller.operationBusy)
                            Button("Show .env in Finder") {
                                NSWorkspace.shared.activateFileViewerSelecting([LocalConfiguration.fileURL])
                            }
                        }
                        Text(configurationStatus).font(.caption)
                        Text("The private .env persists on disk, including any token or Wi-Fi password you put there. Loading it never connects, provisions Wi-Fi, or takes a photo.")
                            .font(.caption).foregroundStyle(.secondary)
                    }.padding(6)
                }
                GroupBox("1 · Choose your glasses") {
                    VStack(alignment: .leading) {
                        HStack {
                            Button(controller.scanning ? "Rescan" : "Scan (10 seconds)") { controller.scan() }
                                .disabled(controller.connected || controller.captureBusy || controller.operationBusy)
                            Button("Connect selected") { controller.connect() }
                                .disabled(controller.selectedID == nil || controller.connected || controller.captureBusy || controller.operationBusy)
                            Button("Disconnect / cancel connection") { controller.disconnect() }
                        }
                        List(selection: $controller.selectedID) {
                            ForEach(controller.devices) { device in
                                VStack(alignment: .leading) {
                                    Text(device.name)
                                    Text(device.id + (device.rssi.map { " · \($0) dBm" } ?? ""))
                                        .font(.caption).foregroundStyle(.secondary)
                                }.tag(device.id)
                            }
                        }.frame(height: 110)
                        Text("Select explicitly; no saved/default device or automatic capture.")
                            .font(.caption)
                    }.padding(6)
                }
                GroupBox("2 · Camera readiness") {
                    VStack(alignment: .leading, spacing: 8) {
                        Text(controller.connected ? (controller.ready ? "Connected · boot ready" : "Connected · waiting for boot") : "Disconnected")
                        Text("SDK \(CaptureCoreVersion.sdkRelease) · Firmware-version validation skipped")
                        if controller.cameraSetupFailed {
                            Button("Retry camera setup") { controller.retryCameraSetup() }
                                .disabled(!controller.ready || controller.operationBusy || controller.captureBusy)
                        }
                        Text("Camera privacy settings are prepared automatically. Connect glasses Wi-Fi, then choose Single photo. No automatic photos or firmware installs. SDK analytics are disabled; startup update checks still contact Mentra.")
                            .font(.caption)
                    }.padding(6)
                }
                transportControls
                GroupBox("4 · Local receiver and snapshots") {
                    VStack(alignment: .leading, spacing: 8) {
                        if controller.uploadMode == .usbADB {
                            TextField("Glasses loopback URL (ADB reverse)", text: $usbWebhook)
                                .disabled(controller.captureBusy || controller.operationBusy)
                                .onChange(of: usbWebhook) { _ in controller.confirmUSBTunnel(false) }
                        } else {
                            TextField("Mac LAN webhook URL (not localhost)", text: $wifiWebhook)
                                .disabled(controller.captureBusy || controller.operationBusy)
                        }
                        SecureField("Receiver bearer token (.env default or session only)", text: $token)
                            .disabled(controller.captureBusy || controller.operationBusy)
                        HStack {
                            Button("Single photo") { request(repeating: false) }
                                .disabled(!controller.captureAllowed)
                            Button("Start serial snapshots") { request(repeating: true) }
                                .disabled(!controller.captureAllowed)
                            Button("Stop") { controller.stop() }
                            Button("Clear session token") { token = "" }.disabled(controller.captureBusy)
                        }
                        Text("Serial requests wait for terminal delivery and processing, then pause 3 seconds. Stop never cancels an issued upload. Sleep/disconnect stops the loop; no automatic resume.")
                            .font(.caption)
                        Text(controller.captureStatus).textSelection(.enabled)
                    }.padding(6)
                }
                Text(controller.status).textSelection(.enabled).accessibilityLabel("Operation status")
                Text("Mac viewer: http://127.0.0.1:8091/. Masking is opt-in and COCO-limited. Raw photos reach the Mac before masking; unselected content is visible. Wi-Fi HTTP requires a trusted LAN. USB mode can use a loopback-only receiver.")
                    .font(.caption).foregroundStyle(.secondary)
            }.padding(20)
        }.frame(minWidth: 700, minHeight: 650)
            .onAppear { reloadConfiguration() }
    }

    private var transportControls: some View {
        GroupBox("3 · Photo upload transport") {
            VStack(alignment: .leading, spacing: 10) {
                Picker("Transport", selection: Binding(
                    get: { controller.uploadMode }, set: { controller.setUploadMode($0) }
                )) {
                    ForEach(CaptureUploadMode.allCases) { mode in Text(mode.title).tag(mode) }
                }.pickerStyle(.segmented)
                if controller.uploadMode == .usbADB {
                    usbControls
                } else {
                    wifiControls
                }
            }.padding(6)
        }.disabled(controller.captureBusy || controller.operationBusy)
    }

    private var wifiControls: some View {
        VStack(alignment: .leading) {
            TextField("SSID (optional .env default)", text: $ssid)
            SecureField("Wi-Fi password (empty for open network)", text: $password)
            Button("Provision glasses Wi-Fi") {
                controller.provisionWiFi(ssid: ssid, password: password)
                ssid = ""
                password = ""
            }.disabled(!controller.ready || ssid.isEmpty)
            Text("The glasses may remember Wi-Fi. Optional .env credentials persist locally; edits here are session-only.")
                .font(.caption)
        }
    }

    private var usbControls: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Bluetooth is still required for camera commands. JPEGs go to the glasses' 127.0.0.1 through ADB reverse, not to the Wi-Fi LAN.")
            Text("Start the receiver with npm run start:usb in Mentra/laptop. Connect a USB data cable and run adb devices -l in Terminal.")
                .font(.caption)
            TextField("USB ADB serial of the SAME glasses selected above", text: $adbSerial)
                .onChange(of: adbSerial) { _ in controller.confirmUSBTunnel(false) }
            Button("Copy USB setup command for Terminal") {
                guard let command = usbSetupCommand else { return }
                NSPasteboard.general.clearContents()
                NSPasteboard.general.setString(command, forType: .string)
            }.disabled(usbSetupCommand == nil)
            Text("Run the copied command in Terminal (outside this sandboxed app). It checks the selected USB device and receiver, and creates only the chosen port mapping. It never takes a photo or installs firmware.")
                .font(.caption)
            Toggle("I successfully ran setup for these glasses and this receiver port", isOn: Binding(
                get: { controller.usbTunnelConfigured },
                set: { controller.confirmUSBTunnel($0) }
            )).disabled(usbSetupCommand == nil || !controller.ready)
            Text("Re-run after reconnecting USB. This confirmation is manual, not a cable monitor. Wi-Fi readiness is skipped only in USB mode; firmware must support HTTP loopback uploads. No BLE image fallback.")
                .font(.caption).foregroundStyle(.secondary)
        }
    }

    private var usbSetupCommand: String? {
        let serial = adbSerial.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !serial.isEmpty, !serial.hasPrefix("-"),
              serial.rangeOfCharacter(from: .whitespacesAndNewlines) == nil,
              let url = try? CaptureConfiguration.validateWebhook(usbWebhook, uploadMode: .usbADB),
              let port = url.port,
              let script = Bundle.main.url(forResource: "usb-debug", withExtension: "sh")
        else { return nil }
        return "bash \(shellQuote(script.path)) \(shellQuote(serial)) \(port)"
    }

    private func shellQuote(_ value: String) -> String {
        "'" + value.replacingOccurrences(of: "'", with: "'\\''") + "'"
    }

    private func request(repeating: Bool) {
        let webhook = controller.uploadMode == .usbADB ? usbWebhook : wifiWebhook
        controller.takePhoto(webhook: webhook, token: token, repeatSnapshots: repeating)
    }

    private func reloadConfiguration() {
        guard !controller.captureBusy, !controller.operationBusy else { return }
        do {
            let configuration = try LocalConfiguration.load()
            wifiWebhook = configuration.wifiWebhook
            usbWebhook = configuration.usbWebhook
            token = configuration.token
            ssid = configuration.ssid
            password = configuration.password
            adbSerial = configuration.adbSerial
            controller.setUploadMode(configuration.uploadMode)
            // Never restore an old USB tunnel confirmation from disk.
            controller.confirmUSBTunnel(false)
            configurationStatus = "Loaded private .env defaults. Field edits are not written back; clearing the token only clears this session."
        } catch {
            // Never display parser errors, lines, or credential-bearing values.
            configurationStatus = "Could not load .env. Check that it exists, is valid UTF-8 under 16 KB, and contains valid URL/token settings. Enter settings manually or edit the file, then reload."
        }
    }
}
