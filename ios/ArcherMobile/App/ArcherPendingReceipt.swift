import Foundation
import Security

enum ArcherPendingReceipt {
    private static var base: [String: Any] {
        [kSecClass as String: kSecClassGenericPassword,
         kSecAttrService as String: "com.gravybets.archer.dev.pending-receipt",
         kSecAttrAccount as String: "owner-request",
         kSecAttrSynchronizable as String: kCFBooleanFalse as Any]
    }
    static func read() -> UUID? {
        var q=base; q[kSecReturnData as String]=kCFBooleanTrue
        q[kSecMatchLimit as String]=kSecMatchLimitOne
        var result: CFTypeRef?
        guard SecItemCopyMatching(q as CFDictionary,&result)==errSecSuccess,
              let data=result as? Data, let text=String(data:data,encoding:.utf8) else { return nil }
        return UUID(uuidString:text)
    }
    static func save(_ id: UUID) -> Bool {
        let data=Data(id.uuidString.utf8)
        var q=base; q[kSecValueData as String]=data
        q[kSecAttrAccessible as String]=kSecAttrAccessibleWhenUnlockedThisDeviceOnly
        let result=SecItemAdd(q as CFDictionary,nil)
        if result==errSecDuplicateItem { return false }
        return result==errSecSuccess
    }
    @discardableResult static func clear() -> Bool {
        let result=SecItemDelete(base as CFDictionary)
        return result==errSecSuccess || result==errSecItemNotFound
    }
}
