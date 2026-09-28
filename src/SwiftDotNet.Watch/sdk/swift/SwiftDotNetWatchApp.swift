import SwiftUI

/// The watch app's entry point. It owns the process (the reverse of iOS, where .NET does): it starts the
/// .NET side, and shows the SwiftDotNetBridge renderer's `RootHostView` — the same interpreter the
/// iOS/macOS/tvOS heads use, compiled into this app for watchOS.
@main
struct SwiftDotNetWatchApp: App {
    @Environment(\.scenePhase) private var scenePhase
    #if SWIFTDOTNET_WATCH_LIVE
    @StateObject private var live = LiveConnection.shared
    #endif

    init() {
        #if SWIFTDOTNET_WATCH_CUSTOM
        swiftDotNetWatchConfigure(SwiftDotNetWatchRegistry.shared)
        #endif
        SwiftDotNetWatch.start()
    }

    var body: some Scene {
        WindowGroup {
            RootHostView()
            #if SWIFTDOTNET_WATCH_LIVE
                .overlay { if !live.connected { LiveWaitingView(port: live.port, message: live.message) } }
            #endif
        }
        .onChange(of: scenePhase) { _, phase in SwiftDotNetWatch.phase(phase) }
    }
}

#if SWIFTDOTNET_WATCH_LIVE
/// Shown while the live shell has no .NET process to talk to.
private struct LiveWaitingView: View {
    let port: UInt16
    let message: String?

    var body: some View {
        VStack(spacing: 6) {
            ProgressView()
            Text("Waiting for .NET").font(.headline)
            Text(message ?? "Run the app with dotnet run or from your IDE (port \(String(port)))")
                .font(.footnote)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
        }
        .padding()
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(.black.opacity(0.9))
    }
}
#endif
