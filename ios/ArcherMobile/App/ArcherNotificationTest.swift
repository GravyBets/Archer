import Combine
import Foundation
import UserNotifications

/// A deliberately local, owner-requested iOS notification test.
/// It is NOT APNs, background delivery, a paid alert, or an Archer incident.
@MainActor
final class ArcherNotificationTest: ObservableObject {
    @Published private(set) var status = "Notifications not requested."
    @Published private(set) var authorized = false
    @Published private(set) var busy = false

    func inspect() async {
        let settings = await UNUserNotificationCenter.current().notificationSettings()
        authorized = settings.authorizationStatus == .authorized ||
                     settings.authorizationStatus == .provisional
        status = authorized
            ? "iPhone has allowed notifications. Remote Archer push is not connected."
            : settings.authorizationStatus == .denied
                ? "Notifications blocked in iPhone Settings. Remote push is still unavailable."
                : "Notifications not requested. Remote Archer push is not connected."
    }

    func requestPermission() async {
        guard !busy else { return }
        busy = true
        defer { busy = false }
        do {
            _ = try await UNUserNotificationCenter.current().requestAuthorization(
                options: [.alert, .sound, .badge])
            await inspect()
        } catch {
            authorized = false
            status = "The permission request failed. Check iPhone notification settings."
        }
    }

    func sendLocalTest() async {
        guard authorized, !busy else { return }
        busy = true
        defer { busy = false }
        let content = UNMutableNotificationContent()
        content.title = "Archer notification test"
        content.body = "Local iPhone notification only. No Archer incident or remote message."
        content.sound = .default
        let trigger = UNTimeIntervalNotificationTrigger(timeInterval: 5, repeats: false)
        let request = UNNotificationRequest(
            identifier: "archer-local-owner-test", content: content, trigger: trigger)
        do {
            // Reuse one stable ID so repeated taps never queue duplicate tests.
            UNUserNotificationCenter.current().removePendingNotificationRequests(
                withIdentifiers: [request.identifier])
            try await UNUserNotificationCenter.current().add(request)
            status = "One local test scheduled for five seconds. This is NOT remote push."
        } catch {
            status = "Could not schedule the local notification test."
        }
    }
}
