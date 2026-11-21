import UIKit
import UserNotifications

/// Presents only explicit, local owner-requested notification tests while
/// Archer is open. Real remote/APNs alert delivery is not connected yet.
final class ArcherNotificationDelegate: NSObject,
    UIApplicationDelegate, UNUserNotificationCenterDelegate {

    func application(
        _ application: UIApplication,
        didFinishLaunchingWithOptions launchOptions:
            [UIApplication.LaunchOptionsKey: Any]? = nil
    ) -> Bool {
        UNUserNotificationCenter.current().delegate = self
        return true
    }

    func userNotificationCenter(
        _ center: UNUserNotificationCenter,
        willPresent notification: UNNotification,
        withCompletionHandler completionHandler:
            @escaping (UNNotificationPresentationOptions) -> Void
    ) {
        if notification.request.identifier == "archer-local-owner-test" {
            completionHandler([.banner, .sound])
        } else {
            // No unreviewed production alerts or background push are enabled.
            completionHandler([])
        }
    }
}
