// Acceso mínimo a la nube SmartHome de Midea: solo para obtener el token y la key una vez.

import Foundation

public final class SmartHomeCloud {
    static let baseURL = "https://mp-prod.appsmb.com"
    static let appID = "1010"
    static let hmacKey = Bytes("PROD_VnoClJI9aikS8dyy".utf8)
    static let iotKey = "meicloud"
    static let loginKey = "ac21b9f9cbfe4ca5a88562ef25e2b768"

    private let deviceID = MideaCrypto.random(8).hex
    private var accessToken = ""

    public init() {}

    // MARK: Firmas (iguales que msmart-ng)

    public static func sign(json: String, random: String) -> String {
        MideaCrypto.hmacSHA256(key: hmacKey, message: Bytes((iotKey + json + random).utf8)).hex
    }

    public static func encryptPassword(loginID: String, password: String) -> String {
        let m1 = MideaCrypto.sha256(Bytes(password.utf8)).hex
        return MideaCrypto.sha256(Bytes((loginID + m1 + loginKey).utf8)).hex
    }

    public static func encryptIAMPassword(loginID: String, password: String) -> String {
        let m1 = MideaCrypto.md5(Bytes(password.utf8)).hex
        let m2 = MideaCrypto.md5(Bytes(m1.utf8)).hex
        return MideaCrypto.sha256(Bytes((loginID + m2 + loginKey).utf8)).hex
    }

    // MARK: Peticiones

    private static func stamp() -> String {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.timeZone = TimeZone(identifier: "UTC")
        f.dateFormat = "yyyyMMddHHmmss"
        return f.string(from: Date())
    }

    private func baseBody(_ extra: [String: Any]) -> [String: Any] {
        var body: [String: Any] = [
            "appId": Self.appID,
            "src": Self.appID,
            "format": 2,
            "clientType": 1,
            "language": "en_US",
            "deviceId": deviceID,
            "stamp": Self.stamp(),
            "reqId": MideaCrypto.random(16).hex,
        ]
        for (k, v) in extra { body[k] = v }
        return body
    }

    private func request(_ endpoint: String, _ body: [String: Any]) async throws -> [String: Any] {
        let jsonData = try JSONSerialization.data(withJSONObject: body)
        let json = String(decoding: jsonData, as: UTF8.self)
        let random = MideaCrypto.random(16).hex

        guard let url = URL(string: "\(Self.baseURL)/mas/v5/app/proxy?alias=\(endpoint)") else {
            throw MideaError.network("URL no válida")
        }
        var req = URLRequest(url: url, timeoutInterval: 15)
        req.httpMethod = "POST"
        req.httpBody = Data(json.utf8)
        req.setValue("application/json", forHTTPHeaderField: "Content-Type")
        req.setValue("1", forHTTPHeaderField: "secretVersion")
        req.setValue(Self.sign(json: json, random: random), forHTTPHeaderField: "sign")
        req.setValue(random, forHTTPHeaderField: "random")
        req.setValue(accessToken, forHTTPHeaderField: "accessToken")

        let (data, response) = try await URLSession.shared.data(for: req)
        guard let http = response as? HTTPURLResponse, http.statusCode == 200 else {
            throw MideaError.network("la nube respondió con error HTTP")
        }
        guard let obj = try JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            throw MideaError.cloud("respuesta no válida", -1)
        }
        let code: Int
        if let n = obj["code"] as? Int { code = n }
        else if let s = obj["code"] as? String, let n = Int(s) { code = n }
        else { code = -1 }
        guard code == 0 else {
            throw MideaError.cloud(obj["msg"] as? String ?? "error desconocido", code)
        }
        return obj["data"] as? [String: Any] ?? [:]
    }

    // MARK: API

    public func login(account: String, password: String) async throws {
        let idResponse = try await request("/v1/user/login/id/get", baseBody(["loginAccount": account]))
        guard let loginID = idResponse["loginId"] as? String else {
            throw MideaError.cloud("no se recibió loginId", -1)
        }

        let body: [String: Any] = [
            "data": [
                "platform": 2,
                "deviceId": deviceID,
            ] as [String: Any],
            "iotData": [
                "appId": Self.appID,
                "src": Self.appID,
                "clientType": 1,
                "loginAccount": account,
                "iampwd": Self.encryptIAMPassword(loginID: loginID, password: password),
                "password": Self.encryptPassword(loginID: loginID, password: password),
                "pushToken": MideaCrypto.random(90).hex,
                "stamp": Self.stamp(),
                "reqId": MideaCrypto.random(16).hex,
            ] as [String: Any],
        ]
        let response = try await request("/mj/user/login", body)
        guard let mdata = response["mdata"] as? [String: Any],
              let token = mdata["accessToken"] as? String
        else { throw MideaError.cloud("no se recibió accessToken", -1) }
        accessToken = token
    }

    public func token(udpid: String, applianceID: UInt64) async throws -> (token: String, key: String) {
        let response = try await request("/v1/iot/secure/getToken",
                                          baseBody(["udpid": udpid, "applianceCodes": String(applianceID)]))
        let list = response["tokenlist"] as? [[String: Any]] ?? []
        for item in list where (item["udpId"] as? String)?.lowercased() == udpid.lowercased() {
            if let t = item["token"] as? String, let k = item["key"] as? String {
                return (t, k)
            }
        }
        throw MideaError.cloud("la cuenta no tiene claves para este aire", -1)
    }
}
