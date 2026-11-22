import SwiftUI
import UserNotifications

// The foreground delegate displays the same explicit local test as iOS does
// in the background. No remote registration, permission request or APNs.
final class ArcherAppDelegate: NSObject, UIApplicationDelegate, UNUserNotificationCenterDelegate {
    func application(_ application: UIApplication,
                     didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]? = nil) -> Bool {
        UNUserNotificationCenter.current().delegate = self
        return true
    }

    nonisolated func userNotificationCenter(_ center: UNUserNotificationCenter,
                                           willPresent notification: UNNotification) async -> UNNotificationPresentationOptions {
        guard notification.request.identifier == "archer-local-owner-test" else { return [] }
        return [.banner, .list, .sound]
    }
}

@main
struct ArcherMobileApp: App {
    @UIApplicationDelegateAdaptor(ArcherAppDelegate.self) private var appDelegate
    @StateObject private var session = ArcherAuthSession()

    var body: some Scene {
        WindowGroup {
            ArcherWelcomeView()
                .environmentObject(session)
        }
    }
}
