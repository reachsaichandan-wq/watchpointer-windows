import SwiftUI
import WatchCore
import WatchKit

/// Owns the session for the app's lifetime.
struct ContentView: View {
    @StateObject private var session = Session()

    var body: some View { RemoteScreen(session: session, link: session.link) }
}

/// A remote you use without looking: one big pad, three buttons in fixed places, haptics for feedback.
private struct RemoteScreen: View {
    @ObservedObject var session: Session
    @ObservedObject var link: PCLink
    @Environment(\.scenePhase) private var scenePhase
    @AppStorage(WatchSettingKey.crownSpeed) private var crownSpeed = 1.0
    @AppStorage(WatchSettingKey.invertCrown) private var invertCrown = false
    @AppStorage(WatchSettingKey.haptics) private var haptics = true
    @State private var crown = 0.0
    @FocusState private var crownFocused: Bool
    @State private var clicks = 0
    @State private var centers = 0
    @State private var confirmUnpair = false
    @State private var unpairTitle = ""

    var body: some View {
        NavigationStack {
            remote
                .navigationTitle(link.connected ? (link.pcProfile ?? "") : "")
                .navigationBarTitleDisplayMode(.inline)
                .toolbar {
                    if link.connected {
                        // Top-left, next to the clock: which PC this is, and the way to unpair it.
                        ToolbarItem(placement: .topBarLeading) {
                            Button {
                                unpairTitle = link.pairedName ?? "PC" // fixed while the dialog is up
                                confirmUnpair = true
                            } label: { Image(systemName: "pc") }
                        }
                        ToolbarItemGroup(placement: .bottomBar) {
                            NavigationLink { RemoteView(link: link) } label: { Image(systemName: "square.grid.2x2") }
                            // The system Double Tap (pinch twice) presses this button on watches that support it.
                            Button { link.sendGesture(.handDoubleTap); clicks += 1 } label: { Image(systemName: "hand.tap") }
                                .handGestureShortcut(.primaryAction)
                            NavigationLink { SettingsView(session: session) } label: { Image(systemName: "gearshape") }
                        }
                    } else {
                        ToolbarItem(placement: .topBarTrailing) {
                            NavigationLink { SettingsView(session: session) } label: { Image(systemName: "gearshape") }
                        }
                    }
                }
                .confirmationDialog(unpairTitle, isPresented: $confirmUnpair, titleVisibility: .visible) {
                    Button("Unpair", role: .destructive) {
                        // Unpair once the dialog has closed; changing the screen under it made it pop up a second time.
                        DispatchQueue.main.asyncAfter(deadline: .now() + 0.35) { link.forget() }
                    }
                }
        }
        .onChange(of: link.connected) { session.connectedChanged() }
        .onChange(of: link.paused) { session.updateMotion() }
        .onChange(of: scenePhase) { _, phase in
            session.foreground = phase != .background
            session.updateMotion()
        }
    }

    private var remote: some View {
        Group {
            if !link.bluetoothReady {
                Label("Turn on Bluetooth", systemImage: "antenna.radiowaves.left.and.right.slash")
                    .multilineTextAlignment(.center)
                    .foregroundStyle(.secondary)
            } else if link.connected {
                VStack(spacing: 6) {
                    ClickPad(paused: link.paused || link.pcPaused, clicks: $clicks, link: link)
                        .frame(maxHeight: .infinity)
                    HStack(spacing: 6) {
                        PadButton(symbol: "contextualmenu.and.cursorarrow") {
                            clicks += 1
                            // Buttons fire on release; a press lasts about this long, so this is roughly touch-down.
                            link.send(.rightClick, at: ProcessInfo.processInfo.systemUptime - 0.12)
                        }
                        PadButton(symbol: "scope") {
                            centers += 1
                            session.center()
                        }
                        PadButton(symbol: link.paused ? "play.fill" : "pause.fill", tint: link.paused ? .orange : .white) {
                            link.togglePause()
                        }
                    }
                    .frame(height: 52)
                }
                .padding(.horizontal, 4)
                .transition(.opacity)
            } else if let name = link.pairedName {
                WaitingForPC(name: name) { link.forget() }
                    .transition(.opacity)
            } else {
                PairList(found: link.found) { link.pair($0) }
                    .transition(.opacity)
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(.black)
        .animation(.smooth(duration: 0.3), value: link.connected)
        // The Crown only reaches the focused view, so keep focus here rather than on a button.
        .focusable()
        .focused($crownFocused)
        .digitalCrownRotation($crown)
        .onChange(of: crown) { old, new in
            guard link.connected else { return }
            link.scroll(by: (new - old) * crownPixelsPerUnit * crownSpeed * (invertCrown ? -1 : 1))
        }
        .onAppear { crownFocused = true }
        .onChange(of: link.connected) { crownFocused = true }
        .sensoryFeedback(.impact(weight: .light), trigger: clicks) { _, _ in haptics }
        .sensoryFeedback(.success, trigger: centers) { _, _ in haptics }
        .sensoryFeedback(.selection, trigger: link.paused) { _, _ in haptics }
        .sensoryFeedback(trigger: link.connected) { _, now in haptics ? (now ? .start : .stop) : nil }
    }
}

/// Not paired yet: every PC running the receiver nearby (closest first), each with a Pair button.
private struct PairList: View {
    let found: [FoundPC]
    let pair: (FoundPC) -> Void

    var body: some View {
        if found.isEmpty {
            VStack(spacing: 8) {
                Image(systemName: "pc")
                    .font(.system(size: 34))
                    .foregroundStyle(.secondary)
                    .symbolEffect(.pulse)
                Text("Start WatchPointer on your PC")
                    .font(.footnote)
                    .multilineTextAlignment(.center)
                    .foregroundStyle(.secondary)
            }
        } else {
            ScrollView {
                VStack(spacing: 6) {
                    ForEach(found) { pc in
                        Button { pair(pc) } label: {
                            HStack(spacing: 8) {
                                Text(pc.name)
                                    .lineLimit(2)
                                    .multilineTextAlignment(.leading)
                                    .frame(maxWidth: .infinity, alignment: .leading)
                                Text("Pair")
                                    .font(.system(size: 14, weight: .semibold))
                                    .foregroundStyle(.black)
                                    .padding(.horizontal, 10).padding(.vertical, 5)
                                    .background(.white, in: Capsule())
                            }
                            .font(.system(size: 15, weight: .medium))
                            .padding(.horizontal, 12)
                            .padding(.vertical, 8)
                            .frame(minHeight: 52)
                            .background(Color(white: 0.16), in: RoundedRectangle(cornerRadius: 16, style: .continuous))
                        }
                        .buttonStyle(PressStyle())
                    }
                }
                .padding(.horizontal, 4)
            }
        }
    }
}

/// Paired, but that PC isn't connected right now (receiver closed, out of range). Connects by itself when it's back.
private struct WaitingForPC: View {
    let name: String
    let forget: () -> Void

    var body: some View {
        VStack(spacing: 10) {
            Spacer()
            Image(systemName: "pc")
                .font(.system(size: 34))
                .foregroundStyle(.secondary)
                .symbolEffect(.pulse)
            Text(name)
                .font(.system(size: 15, weight: .medium))
                .foregroundStyle(Color(white: 0.6))
                .lineLimit(1)
            Spacer()
            Button(action: forget) {
                Text("Forget")
                    .font(.system(size: 15, weight: .semibold))
                    .frame(maxWidth: .infinity)
                    .frame(height: 44)
                    .background(Color(white: 0.16), in: RoundedRectangle(cornerRadius: 16, style: .continuous))
            }
            .buttonStyle(PressStyle())
        }
        .padding(.horizontal, 4)
    }
}

struct PadButton: View {
    let symbol: String
    var tint: Color = .white
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            RoundedRectangle(cornerRadius: 16, style: .continuous)
                .fill(Color(white: 0.16))
                .overlay {
                    Image(systemName: symbol)
                        .font(.system(size: 18, weight: .medium))
                        .foregroundStyle(tint)
                        .contentTransition(.symbolEffect(.replace))
                }
        }
        .buttonStyle(PressStyle())
    }
}

struct PressStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .opacity(configuration.isPressed ? 0.6 : 1)
            .scaleEffect(configuration.isPressed ? 0.96 : 1)
            .animation(.smooth(duration: 0.12), value: configuration.isPressed)
    }
}
