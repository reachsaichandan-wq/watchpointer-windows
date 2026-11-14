import SwiftUI
import WatchCore

/// Four big buttons whose actions are chosen per profile on the PC (Gestures page). Defaults: Desktop = Alt+Tab,
/// Enter, snap left, snap right; Presentation = start, end, black screen, laser; Media = previous, play/pause, next, mute.
struct RemoteView: View {
    @ObservedObject var link: PCLink
    @State private var presses = 0

    private let buttons: [(Gesture, String, String)] = [
        (.buttonA, "A", "a.circle.fill"),
        (.buttonB, "B", "b.circle.fill"),
        (.buttonC, "C", "c.circle.fill"),
        (.buttonD, "D", "d.circle.fill"),
    ]

    var body: some View {
        VStack(spacing: 6) {
            if let profile = link.pcProfile {
                Text(profile).font(.footnote).foregroundStyle(.secondary)
            }
            Grid(horizontalSpacing: 6, verticalSpacing: 6) {
                GridRow { button(0); button(1) }
                GridRow { button(2); button(3) }
            }
            if !link.pcSupportsV2 {
                Text("This PC's receiver doesn't support remote buttons.")
                    .font(.footnote).foregroundStyle(.orange).multilineTextAlignment(.center)
            }
        }
        .padding(.horizontal, 4)
        .sensoryFeedback(.impact(weight: .light), trigger: presses)
        .navigationTitle("Remote")
    }

    private func button(_ index: Int) -> some View {
        let (gesture, label, symbol) = buttons[index]
        return PadButton(symbol: symbol) {
            presses += 1
            link.sendGesture(gesture)
        }
        .accessibilityLabel("Remote button \(label)")
    }
}
