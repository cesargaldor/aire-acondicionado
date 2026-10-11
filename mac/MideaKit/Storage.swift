// Guarda la configuración del aire (con el token y la key) en el Llavero del Mac.

import Foundation
import Security

public enum ConfigStore {
    static let service = "local.aire.menubar"
    static let account = "device"

    public static func load() -> DeviceConfig? {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne,
        ]
        var item: CFTypeRef?
        guard SecItemCopyMatching(query as CFDictionary, &item) == errSecSuccess,
              let data = item as? Data
        else { return nil }
        return try? JSONDecoder().decode(DeviceConfig.self, from: data)
    }

    @discardableResult
    public static func save(_ config: DeviceConfig) -> Bool {
        guard let data = try? JSONEncoder().encode(config) else { return false }
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
        ]
        let update: [String: Any] = [kSecValueData as String: data]
        var status = SecItemUpdate(query as CFDictionary, update as CFDictionary)
        if status == errSecItemNotFound {
            var add = query
            add[kSecValueData as String] = data
            add[kSecAttrLabel as String] = "Aire (aire acondicionado)"
            status = SecItemAdd(add as CFDictionary, nil)
        }
        return status == errSecSuccess
    }

    public static func delete() {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
        ]
        SecItemDelete(query as CFDictionary)
    }

    /// Lee un config.json en formato {"ip", "id", "token", "key"} (el de msmart-ng y similares).
    public static func importPythonConfig(_ data: Data) -> DeviceConfig? {
        guard let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let ip = obj["ip"] as? String
        else { return nil }
        let id: UInt64
        if let n = obj["id"] as? NSNumber { id = n.uint64Value }
        else if let s = obj["id"] as? String, let n = UInt64(s) { id = n }
        else { return nil }
        let token = obj["token"] as? String
        let key = obj["key"] as? String
        // El puerto de config.json es el de la web (8080); el aire usa el 6444
        return DeviceConfig(ip: ip, port: 6444, id: id,
                            version: (token != nil && key != nil) ? 3 : 2,
                            token: token, key: key)
    }
}
