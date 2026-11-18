import SwiftUI

struct ArcherWelcomeView: View {
    @EnvironmentObject private var session: ArcherAuthSession

    var body: some View {
        ZStack {
            LinearGradient(
                colors: [Color(red: 0.035, green: 0.055, blue: 0.11),
                         Color(red: 0.08, green: 0.12, blue: 0.20)],
                startPoint: .topLeading, endPoint: .bottomTrailing
            ).ignoresSafeArea()

            ScrollView {
                VStack(alignment: .leading, spacing: 24) {
                    HStack(spacing: 12) {
                        Image(systemName: "network.badge.shield.half.filled")
                            .font(.system(size: 30, weight: .light))
                            .foregroundStyle(.cyan)
                        Text("ARCHER")
                            .font(.system(size: 23, weight: .semibold, design: .rounded))
                            .tracking(4)
                    }
                    .accessibilityElement(children: .combine)

                    Text("Your operator.\nYour control.")
                        .font(.system(size: 40, weight: .bold, design: .rounded))
                        .tracking(-1.5)
                        .fixedSize(horizontal: false, vertical: true)

                    Text("Secure owner sign-in • Development preview")
                        .foregroundStyle(.white.opacity(0.7))
                        .font(.subheadline)

                    Divider().overlay(.white.opacity(0.3))

                    Label(
                        session.signedIn ? "Auth0 session present" : "Owner sign-in required",
                        systemImage: session.signedIn ? "checkmark.shield" : "lock.shield"
                    )
                    .foregroundStyle(session.signedIn ? .green : .cyan)
                    .font(.headline)

                    Text(session.message)
                        .font(.body)
                        .foregroundStyle(session.hadFailure ? .orange : .white.opacity(0.88))
                        .accessibilityIdentifier("sessionStatus")

                    VStack(alignment: .leading, spacing: 12) {
                        Label("Auth0 authentication", systemImage: "checkmark.circle")
                        Label("Archer device enrollment: not connected", systemImage: "lock")
                        Label("Remote conversations: unavailable", systemImage: "lock")
                        Label("Calls, SMS and paid actions: disabled", systemImage: "lock")
                    }
                    .font(.subheadline)
                    .foregroundStyle(.white.opacity(0.7))

                    if !ArcherAuthConfiguration.configured {
                        Text("Finish the Auth0 Native Client ID configuration before running this build.")
                            .font(.footnote)
                            .foregroundStyle(.orange)
                    }

                    if session.busy {
                        ProgressView().tint(.cyan)
                    } else if session.signedIn {
                        Button("Check / renew session") {
                            Task { await session.refreshSession() }
                        }
                        .buttonStyle(.borderedProminent)
                        .tint(.cyan)
                        Button("Sign out") {
                            Task { await session.signOut() }
                        }
                        .buttonStyle(.bordered)
                        .tint(.white)
                    } else {
                        Button("Sign in with Auth0") {
                            Task { await session.signIn() }
                        }
                        .buttonStyle(.borderedProminent)
                        .tint(.cyan)
                        .disabled(!ArcherAuthConfiguration.configured)
                    }

                    Text("Signing in only proves an Auth0 session on this phone. "
                         + "Archer won't accept commands until the gateway separately "
                         + "validates the owner and enrolled device.")
                        .font(.caption)
                        .foregroundStyle(.white.opacity(0.55))
                }
                .frame(maxWidth: 580, alignment: .leading)
                .padding(30)
                .frame(maxWidth: .infinity, alignment: .top)
            }
        }
        .preferredColorScheme(.dark)
    }
}
