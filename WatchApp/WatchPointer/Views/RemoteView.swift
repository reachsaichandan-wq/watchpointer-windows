import SwiftUI
import WatchCore

/// Tools: Precision, Drag lock and Display (fixed functions), then eight buttons whose actions and labels come from
/// the PC's active profile (Desktop: Desktop, App switch, Task View, Screenshot, Esc, Enter, Keyboard, Lock PC;
/// Presentation and Media have their own). Pointing keeps working while this screen is open.
struct RemoteView: View {
    @ObservedObject var link: PCLink
    @State private var presses = 0

    private let fallback = ["A", "B", "C", "D", "E", "F", "G", "H"]

    private var precisionOn: Bool { link.profileState?.mode == "Precision" }
    private var dragOn: Bool { link.profileState?.dragLock ?? false }

    var body: some View {
        ScrollView {
            VStack(spacing: 6) {
                if let profile = link.profileState?.profile ?? link.pcProfile {
                    Text(profile + (link.profileState.map { $0.mode == "Normal" ? "" : " · \($0.mode)" } ?? ""))
                        .font(.footnote).foregroundStyle(.secondary)
                }
                HStack(spacing: 6) {
                    ToolButton(title: "Precision", symbol: "scope", on: precisionOn) { press(.precisionToggle) }
                    ToolButton(title: "Drag", symbol: "hand.raised.fill", on: dragOn) { press(.dragLockToggle) }
                    ToolButton(title: "Display", symbol: "display.2", on: false) { press(.nextDisplay) }
                }
                LazyVGrid(columns: [GridItem(.flexible(), spacing: 6), GridItem(.flexible(), spacing: 6)], spacing: 6) {
                    ForEach(Array(WatchGesture.remoteButtons.enumerated()), id: \.offset) { index, gesture in
                        LabelButton(title: link.profileState?.label(for: gesture) ?? fallback[index]) { press(gesture) }
                    }
                }
                if !link.pcSupportsV2 {
                    Text("This PC's receiver is too old for tool buttons.")
                        .font(.footnote).foregroundStyle(.orange).multilineTextAlignment(.center)
                }
            }
            .padding(.horizontal, 4)
        }
        .sensoryFeedback(.impact(weight: .light), trigger: presses)
        .navigationTitle("Tools")
        .onAppear { link.refreshProfile() }
    }

    private func press(_ gesture: WatchGesture) {
        presses += 1
        link.sendGesture(gesture)
        if gesture == .precisionToggle || gesture == .dragLockToggle {
            // The PC confirms with a state notification; read the new state shortly after as well.
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.25) { link.refreshProfile() }
        }
    }
}

private struct ToolButton: View {
    let title: String
    let symbol: String
    let on: Bool
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            VStack(spacing: 2) {
                Image(systemName: symbol).font(.system(size: 16, weight: .medium))
                Text(title).font(.system(size: 11, weight: .semibold)).lineLimit(1).minimumScaleFactor(0.7)
            }
            .foregroundStyle(on ? .black : .white)
            .frame(maxWidth: .infinity, minHeight: 50)
            .background(on ? Color.white : Color(white: 0.16), in: RoundedRectangle(cornerRadius: 14, style: .continuous))
        }
        .buttonStyle(PressStyle())
        .accessibilityValue(on ? "On" : "Off")
    }
}

private struct LabelButton: View {
    let title: String
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Text(title)
                .font(.system(size: 14, weight: .semibold))
                .lineLimit(2)
                .minimumScaleFactor(0.7)
                .multilineTextAlignment(.center)
                .frame(maxWidth: .infinity, minHeight: 44)
                .background(Color(white: 0.16), in: RoundedRectangle(cornerRadius: 14, style: .continuous))
        }
        .buttonStyle(PressStyle())
    }
}
