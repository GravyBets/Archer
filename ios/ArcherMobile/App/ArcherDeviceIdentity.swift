import Combine
import CryptoKit
import Foundation
import Security

/// Owner-controlled, local Secure Enclave device identity preparation.
/// No server-side enrollment, Auth0 token exchange, or remote permission.
@MainActor
final class ArcherDeviceIdentity: ObservableObject {
    @Published private(set) var prepared = false
    @Published private(set) var fingerprint = ""
    @Published private(set) var message = "No native device key has been created."
    @Published private(set) var busy = false

    private let service = "com.gravybets.archer.dev.device-identity"
    private let account = "owner-p256-v1"

    init() {
        refresh()
    }

    private var query: [String: Any] {
        [kSecClass as String: kSecClassGenericPassword,
         kSecAttrService as String: service,
         kSecAttrAccount as String: account,
         kSecAttrSynchronizable as String: kCFBooleanFalse as Any]
    }

    private func read() throws -> SecureEnclave.P256.Signing.PrivateKey? {
        var search = query
        search[kSecReturnData as String] = kCFBooleanTrue
        search[kSecMatchLimit as String] = kSecMatchLimitOne
        var value: CFTypeRef?
        let result = SecItemCopyMatching(search as CFDictionary, &value)
        if result == errSecItemNotFound { return nil }
        guard result == errSecSuccess, let bytes = value as? Data
        else { throw IdentityError.storage }
        return try SecureEnclave.P256.Signing.PrivateKey(dataRepresentation: bytes)
    }

    private func describe(_ key: SecureEnclave.P256.Signing.PrivateKey?) {
        prepared = key != nil
        if let key {
            let hash = SHA256.hash(data: key.publicKey.x963Representation)
            fingerprint = String(hash.map { String(format: "%02X", $0) }
                                    .joined().prefix(20)) + "…"
            message = "Local Secure Enclave key ready. NOT enrolled or approved by Archer."
        } else {
            fingerprint = ""
            message = SecureEnclave.isAvailable
                ? "Prepare a hardware-backed key on this iPhone. No remote access is granted."
                : "Secure Enclave unavailable on this device or simulator. No weaker fallback is created."
        }
    }

    func refresh() {
        do { describe(try read()) }
        catch {
            prepared = false
            fingerprint = ""
            message = "Device identity cannot be read securely. No remote access is enabled."
        }
    }

    func prepare() {
        guard !busy else { return }
        busy = true
        defer { busy = false }
        guard SecureEnclave.isAvailable else {
            describe(nil)
            return
        }
        do {
            if try read() != nil {
                refresh()
                return
            }
            let key = try SecureEnclave.P256.Signing.PrivateKey()
            var insert = query
            insert[kSecValueData as String] = key.dataRepresentation
            insert[kSecAttrAccessible as String] = kSecAttrAccessibleWhenUnlockedThisDeviceOnly
            let result = SecItemAdd(insert as CFDictionary, nil)
            guard result == errSecSuccess else { throw IdentityError.storage }
            describe(try read())
        } catch {
            prepared = false
            fingerprint = ""
            message = "Could not create and persist the device key securely."
        }
    }

    func testLocalProof() {
        guard !busy else { return }
        busy = true
        defer { busy = false }
        do {
            guard let key = try read() else {
                describe(nil)
                return
            }
            let challenge = Data("ARCHER_NATIVE_DEVICE_LOCAL_TEST_V1".utf8)
            let signature = try key.signature(for: challenge)
            guard key.publicKey.isValidSignature(signature, for: challenge)
            else { throw IdentityError.invalidSignature }
            describe(key)
            message = "Local hardware-key signature verified. This is NOT server enrollment."
        } catch {
            message = "Device key verification failed; no access was granted."
        }
    }

    func eraseLocalKey() {
        guard !busy else { return }
        busy = true
        defer { busy = false }
        let result = SecItemDelete(query as CFDictionary)
        guard result == errSecSuccess || result == errSecItemNotFound else {
            message = "Key erasure could not be verified. Retry before relying on this device."
            return
        }
        describe(nil)
        message = "Local device key erased. Future server-side enrollment would need separate revocation."
    }

    private enum IdentityError: Error {
        case storage
        case invalidSignature
    }
}
