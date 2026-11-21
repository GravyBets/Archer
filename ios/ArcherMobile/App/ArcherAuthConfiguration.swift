import Foundation

/// These are PUBLIC OIDC identifiers, not a client secret.
/// The SDK also reads Domain and ClientId from the bundled Auth0.plist.
enum ArcherAuthConfiguration {
    static let bundleIdentifier = "com.gravybets.archer.dev"
    static let auth0Domain = "dev-1l3ic41kxj7zpeb8.us.auth0.com"
    static let apiAudience = "https://archer.example/api/dev"
    static let requestedScope =
        "openid profile email offline_access conversation:read conversation:write"

    static var configured: Bool {
        guard let url = Bundle.main.url(forResource: "Auth0", withExtension: "plist"),
              let values = NSDictionary(contentsOf: url),
              let domain = values["Domain"] as? String,
              let clientId = values["ClientId"] as? String
        else { return false }

        return domain == auth0Domain &&
            clientId.count >= 12 &&
            clientId != "REPLACE_WITH_AUTH0_NATIVE_APP_CLIENT_ID" &&
            Bundle.main.bundleIdentifier == bundleIdentifier
    }

    static var expectedCallback: String {
        "\(bundleIdentifier)://\(auth0Domain)/ios/\(bundleIdentifier)/callback"
    }
}
