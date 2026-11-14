import Combine
import SwiftUI
import WatchCore
import WatchKit

struct SettingsView: View {
    @ObservedObject var session: Session
    @AppStorage(WatchSettingKey.crownSpeed) private var crownSpeed = 1.0
    @AppStorage(WatchSettingKey.invertCrown) private var invertCrown = false
    @AppStorage(WatchSettingKey.swipes) private var swipes = true
    @AppStorage(WatchSettingKey.wristFlick) private var wristFlick = false
    @AppStorage(WatchSettingKey.shake) private var shake = true
    @AppStorage(WatchSettingKey.twist) private var twist = false
    @AppStorage(WatchSettingKey.lowerToPause) private var lowerToPause = true
    @AppStorage(WatchSettingKey.driftCompensation) private var driftCompensation = false
    @AppStorage(WatchSettingKey.haptics) private var haptics = true
    @State private var battery = -1

    var body: some View {
        List {
            Section("Pointer") {
                NavigationLink("Calibrate & center") { CalibrationView(session: session) }
                Toggle("Drift trim", isOn: $driftCompensation)
                    .onChange(of: driftCompensation) { session.applySettings() }
                Text("Learns leftover gyro drift while you hold still. Sensitivity is set on the PC.")
                    .font(.footnote).foregroundStyle(.secondary)
            }
            Section("Crown scrolling") {
                VStack(alignment: .leading) {
                    Text("Speed \(crownSpeed, specifier: "%.1f")×")
                    Slider(value: $crownSpeed, in: 0.2...4, step: 0.2)
                }
                Toggle("Invert", isOn: $invertCrown)
            }
            Section("Gestures") {
                Toggle("Swipes on the pad", isOn: $swipes)
                Toggle("Shake → Escape", isOn: $shake)
                    .onChange(of: shake) { session.applySettings() }
                Toggle("Lower to pause", isOn: $lowerToPause)
                    .onChange(of: lowerToPause) { session.applySettings() }
                Text("Point at the floor to pause the cursor; raise the watch to continue.")
                    .font(.footnote).foregroundStyle(.secondary)
                Toggle("Wrist flick left/right", isOn: $wristFlick)
                    .onChange(of: wristFlick) { session.applySettings() }
                Toggle("Twist for volume", isOn: $twist)
                    .onChange(of: twist) { session.applySettings() }
                Text("Flick and twist are off by default: brisk pointing can come close to a flick, and twisting is easy to do by accident. Actions are set per profile on the PC.")
                    .font(.footnote).foregroundStyle(.secondary)
                Toggle("Haptics", isOn: $haptics)
            }
            Section("Status") {
                LabeledContent("PC", value: session.link.pairedName ?? "Not paired")
                LabeledContent("Connected", value: session.link.connected ? "Yes" : "No")
                LabeledContent("Battery", value: battery >= 0 ? "\(battery)%" : "Unknown")
                LabeledContent("Motion", value: session.motion.isAvailable ? (session.motion.running ? "Running" : "Idle") : "Unavailable")
                LabeledContent("Protocol", value: "v\(protocolVersion)")
                if session.link.pairedName != nil {
                    Button("Forget this PC", role: .destructive) { session.link.forget() }
                }
            }
        }
        .navigationTitle("Settings")
        .onAppear {
            let device = WKInterfaceDevice.current()
            device.isBatteryMonitoringEnabled = true
            battery = device.batteryLevel < 0 ? -1 : Int((device.batteryLevel * 100).rounded())
        }
    }
}

/// Recenter with a live read-out, plus a quick check that up/down and left/right respond the right way.
struct CalibrationView: View {
    @ObservedObject var session: Session
    @State private var left = 0.0
    @State private var up = 0.0
    @State private var centers = 0
    private let timer = Timer.publish(every: 0.1, on: .main, in: .common).autoconnect()

    var body: some View {
        ScrollView {
            VStack(spacing: 10) {
                Text("Hold the watch like a remote, 12 o'clock toward the screen. Point at the middle and tap Center.")
                    .font(.footnote)
                    .multilineTextAlignment(.center)
                HStack {
                    VStack { Text("Left").font(.caption2).foregroundStyle(.secondary); Text("\(left, specifier: "%+.1f")°").monospacedDigit() }
                    Spacer()
                    VStack { Text("Up").font(.caption2).foregroundStyle(.secondary); Text("\(up, specifier: "%+.1f")°").monospacedDigit() }
                }
                .padding(.horizontal, 8)
                Button {
                    centers += 1
                    session.center()
                } label: {
                    Label("Center", systemImage: "scope").frame(maxWidth: .infinity)
                }
                .tint(.blue)
                Text("Turn left: Left grows. Tip up: Up grows. For edge-to-edge sensitivity, run Calibration on the PC.")
                    .font(.footnote)
                    .foregroundStyle(.secondary)
                    .multilineTextAlignment(.center)
            }
        }
        .navigationTitle("Calibrate")
        .sensoryFeedback(.success, trigger: centers)
        .onReceive(timer) { _ in
            left = session.motion.tracker.leftDegrees
            up = session.motion.tracker.upDegrees
        }
    }
}
