import Auth0
import Combine
import Foundation

/// The only capability in this iOS milestone is Auth0 identity sign-in.
/// A valid session is NOT yet Archer gateway authorization or device approval.
/// Tokens are never displayed, printed, sent to diagnostics or stored in defaults.
@MainActor
final class ArcherAuthSession: ObservableObject {
    @Published private(set) var signedIn = false
    @Published private(set) var busy = false
    @Published private(set) var message = "Sign in with your verified Auth0 owner account."
    @Published private(set) var hadFailure = false

    // One shared credentials manager; the Auth0 SDK persists encrypted
    // Keychain credentials and handles refresh-token rotation/renewal.
    private let credentials = CredentialsManager(authentication: Auth0.authentication())

    init() {
        // This is a local Keychain status check only, NOT server-side proof.
        signedIn = credentials.hasValid(minTTL: 60) || credentials.canRenew()
        if signedIn {
            message = "Auth0 credentials found on this device. Gateway access is still disabled."
        }
    }

    func signIn() async {
        guard !busy else { return }
        guard ArcherAuthConfiguration.configured else {
            hadFailure = true
            message = "The Native Client ID or bundle identifier has not been configured."
            return
        }
        busy = true
        defer { busy = false }

        do {
            // Auth0.swift handles the system browser, Authorization Code + PKCE,
            // state/nonce validation and the configured custom-scheme callback.
            // We intentionally do not request the experimental HTTPS callback.
            let result = try await Auth0.webAuth()
                .audience(ArcherAuthConfiguration.apiAudience)
                .scope(ArcherAuthConfiguration.requestedScope)
                .start()

            // Auth0.swift 3.3.1 credentials manager uses iOS Keychain.
            try credentials.store(credentials: result)
            signedIn = true
            hadFailure = false
            message = "Auth0 sign-in completed. Device enrollment and gateway access are not enabled."
        } catch {
            hadFailure = true
            message = "Auth0 sign-in did not complete. Check the configured callback and your account."
            // Never log or display raw Auth0 errors, which may contain URLs.
        }
    }

    // Used only by the fixed HTTPS private gateway client. Never return tokens to UI.
    func gatewayAccessToken() async throws -> String {
        guard signedIn else { throw URLError(.userAuthenticationRequired) }
        let current = try await credentials.credentials(minTTL: 60)
        return current.accessToken
    }

    func refreshSession() async {
        guard !busy, signedIn else { return }
        busy = true
        defer { busy = false }
        do {
            // CredentialsManager renews tokens automatically when needed and
            // persists rotated refresh tokens safely to Keychain.
            _ = try await credentials.credentials(minTTL: 60)
            hadFailure = false
            message = "Auth0 session is available. Gateway access still awaits device enrollment."
        } catch {
            signedIn = false
            hadFailure = true
            message = "Session could not be renewed. Sign in again to continue."
        }
    }

    func signOut() async {
        guard !busy else { return }
        busy = true
        defer { busy = false }

        var remoteLogoutSucceeded = false
        do {
            try await Auth0.webAuth().logout()
            remoteLogoutSucceeded = true
        } catch {
            // The browser may be offline. Always attempt to clear local Keychain.
        }

        do {
            try credentials.clear()
            signedIn = false
            hadFailure = !remoteLogoutSucceeded
            message = remoteLogoutSucceeded
                ? "Signed out of Auth0 and cleared this device's credentials."
                : "Device credentials cleared. The Auth0 browser session may still be active."
        } catch {
            hadFailure = true
            message = "Could not clear Keychain credentials. Lock the device and retry sign-out."
        }
    }
}
