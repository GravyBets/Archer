import SwiftUI

@main
struct ArcherMobileApp: App {
    @StateObject private var session = ArcherAuthSession()

    var body: some Scene {
        WindowGroup {
            ArcherWelcomeView()
                .environmentObject(session)
        }
    }
}
