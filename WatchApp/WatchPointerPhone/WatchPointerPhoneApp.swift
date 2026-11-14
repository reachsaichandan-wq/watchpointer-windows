import SwiftUI

/// Minimal iPhone companion. It exists only so the watch app can be packaged in a standard iPhone .ipa (Payload/
/// WatchPointerPhone.app/Watch/WatchPointer.app), the format Windows sideloading tools expect. The watch app runs on
/// its own and talks to the PC directly; nothing here is needed at runtime.
@main
struct WatchPointerPhoneApp: App {
    var body: some Scene {
        WindowGroup { PhoneHomeView() }
    }
}

struct PhoneHomeView: View {
    var body: some View {
        NavigationStack {
            List {
                Section {
                    Label("Use WatchPointer on your Apple Watch", systemImage: "applewatch")
                        .font(.headline)
                    Text("This iPhone app only carries the watch app. Open WatchPointer on the watch, start WatchPointer on your Windows PC, and tap Pair next to your PC's name.")
                }
                Section("If the watch app isn't on your watch") {
                    Text("Open the Watch app on this iPhone › My Watch › scroll to Available Apps › WatchPointer › Install.")
                }
                Section("Tips") {
                    Text("Hold the watch like a remote with 12 o'clock toward the screen. Point at the middle of the screen and tap Center.")
                    Text("Turn off Wrist Detection (Watch app › Passcode) so the watch doesn't lock when you take it off.")
                }
            }
            .navigationTitle("WatchPointer")
        }
    }
}
