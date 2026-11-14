import SwiftUI
import WatchCore

/// Tap = click. Hold = grab (mouse down); point to move; let go to drop. Quick swipe = gesture (mapped on the PC).
/// Touch times use ProcessInfo.systemUptime, the same clock as CoreMotion timestamps, so the PC can place clicks
/// where the pointer was when the thumb touched down.
struct ClickPad: View {
    let paused: Bool
    @Binding var clicks: Int
    let link: PCLink

    @AppStorage(WatchSettingKey.swipes) private var swipes = true
    @State private var classifier = PadGestureClassifier()
    @State private var pressing = false
    @State private var dragging = false
    @State private var holdTimer: Timer?

    private var fill: Color {
        if dragging { return .white }
        if paused { return Color.orange.opacity(0.25) }
        return Color(white: pressing ? 0.28 : 0.16)
    }

    private var symbol: String { dragging ? "hand.raised.fill" : paused ? "pause.fill" : "cursorarrow.click" }

    var body: some View {
        RoundedRectangle(cornerRadius: 22, style: .continuous)
            .fill(fill)
            .overlay {
                Image(systemName: symbol)
                    .font(.system(size: 28, weight: .medium))
                    .foregroundStyle(dragging ? .black : paused ? .orange : Color(white: 0.55))
                    .contentTransition(.symbolEffect(.replace))
            }
            .scaleEffect(pressing && !dragging ? 0.98 : 1)
            .animation(.smooth(duration: 0.15), value: pressing)
            .animation(.smooth(duration: 0.2), value: dragging)
            .contentShape(Rectangle())
            .gesture(
                DragGesture(minimumDistance: 0)
                    .onChanged { value in
                        let now = ProcessInfo.processInfo.systemUptime
                        if !pressing {
                            pressing = true
                            classifier.swipesEnabled = swipes
                            handle(classifier.touchDown(at: now, x: value.startLocation.x, y: value.startLocation.y))
                            // Fires the hold even if the finger doesn't move (DragGesture only reports changes).
                            holdTimer = Timer.scheduledTimer(withTimeInterval: classifier.holdDelay, repeats: false) { _ in
                                handle(classifier.tick(at: ProcessInfo.processInfo.systemUptime))
                            }
                        } else {
                            handle(classifier.touchMoved(at: now, x: value.location.x, y: value.location.y))
                        }
                    }
                    .onEnded { value in
                        holdTimer?.invalidate()
                        pressing = false
                        handle(classifier.touchUp(at: ProcessInfo.processInfo.systemUptime, x: value.location.x, y: value.location.y))
                    }
            )
            .sensoryFeedback(trigger: dragging) { _, now in .impact(weight: now ? .heavy : .light) }
    }

    private func handle(_ events: [PadEvent]) {
        for event in events {
            switch event {
            case let .tap(touchDown):
                clicks += 1
                link.send(.leftClick, at: touchDown)
            case let .holdStart(touchDown):
                dragging = true
                link.send(.drag, 1, at: touchDown)
            case let .holdEnd(at):
                dragging = false
                link.send(.drag, 0, at: at)
            case let .swipe(gesture, at):
                clicks += 1
                link.sendGesture(gesture, at: at)
            }
        }
    }
}
