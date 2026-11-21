import SwiftUI

@main
struct ArcherMobileApp: App {
    @StateObject private var session = ArcherAuthSession()
    @UIApplicationDelegateAdaptor(ArcherNotificationDelegate.self) private var appDelegate

    var body: some Scene {
        WindowGroup {
            ArcherWelcomeView()
                .environmentObject(session)
        }
    }
}
