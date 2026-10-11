// Cifrado y utilidades de bytes usadas por el protocolo de Midea.

import CommonCrypto
import CryptoKit
import Foundation

public typealias Bytes = [UInt8]

public enum MideaError: Error, CustomStringConvertible {
    case crypto
    case protocolError(String)
    case authentication(String)
    case timeout
    case network(String)
    case cloud(String, Int)
    case notFound

    public var description: String {
        switch self {
        case .crypto: return "Error de cifrado"
        case .protocolError(let m): return "Error de protocolo: \(m)"
        case .authentication(let m): return "Error de autenticación: \(m)"
        case .timeout: return "El aire no responde"
        case .network(let m): return "Error de red: \(m)"
        case .cloud(let m, let c): return "Nube de Midea (\(c)): \(m)"
        case .notFound: return "No se encontró ningún aire en la red"
        }
    }
}

// MARK: - Hex

public extension Array where Element == UInt8 {
    init?(hex: String) {
        let chars = Array(hex.utf8)
        guard chars.count % 2 == 0 else { return nil }
        var out = Bytes()
        out.reserveCapacity(chars.count / 2)
        var i = 0
        while i < chars.count {
            guard let hi = Self.nibble(chars[i]), let lo = Self.nibble(chars[i + 1]) else { return nil }
            out.append(hi << 4 | lo)
            i += 2
        }
        self = out
    }

    var hex: String {
        let digits = Array("0123456789abcdef".utf8)
        var out = Bytes()
        out.reserveCapacity(count * 2)
        for b in self {
            out.append(digits[Int(b >> 4)])
            out.append(digits[Int(b & 0xF)])
        }
        return String(decoding: out, as: UTF8.self)
    }

    private static func nibble(_ c: UInt8) -> UInt8? {
        switch c {
        case 48...57: return c - 48
        case 97...102: return c - 87
        case 65...70: return c - 55
        default: return nil
        }
    }
}

// MARK: - Cifrado

public enum MideaCrypto {
    /// Clave de firma de los paquetes V2 y clave AES derivada de ella.
    public static let signKey = Bytes("xhdiwjnchekd4d512chdjx5d8e4c394D2D7S".utf8)
    public static let encKey = md5(signKey)

    public static func sha256(_ data: Bytes) -> Bytes { Bytes(SHA256.hash(data: data)) }
    public static func md5(_ data: Bytes) -> Bytes { Bytes(Insecure.MD5.hash(data: data)) }

    public static func hmacSHA256(key: Bytes, message: Bytes) -> Bytes {
        let mac = HMAC<SHA256>.authenticationCode(for: message, using: SymmetricKey(data: key))
        return Bytes(mac)
    }

    public static func xor(_ a: Bytes, _ b: Bytes) -> Bytes {
        zip(a, b).map { $0 ^ $1 }
    }

    public static func random(_ count: Int) -> Bytes {
        var rng = SystemRandomNumberGenerator()
        return (0..<count).map { _ in UInt8.random(in: 0...255, using: &rng) }
    }

    /// AES sin relleno (los datos deben ser múltiplo de 16).
    private static func aes(_ op: Int, key: Bytes, iv: Bytes?, data: Bytes) throws -> Bytes {
        guard data.count % 16 == 0 else { throw MideaError.crypto }
        if data.isEmpty { return [] }
        let options = iv == nil ? CCOptions(kCCOptionECBMode) : CCOptions(0)
        let ivBytes = iv ?? Bytes(repeating: 0, count: 16)
        var out = Bytes(repeating: 0, count: data.count + kCCBlockSizeAES128)
        let outCount = out.count
        var moved = 0
        let status = CCCrypt(CCOperation(op), CCAlgorithm(kCCAlgorithmAES), options,
                             key, key.count, ivBytes,
                             data, data.count,
                             &out, outCount, &moved)
        guard status == CCCryptorStatus(kCCSuccess) else { throw MideaError.crypto }
        return Bytes(out[0..<moved])
    }

    /// AES-ECB con relleno PKCS7 y la clave fija de Midea (tramas dentro del paquete V2).
    public static func encryptECB(_ data: Bytes) throws -> Bytes {
        let pad = 16 - data.count % 16
        return try aes(kCCEncrypt, key: encKey, iv: nil, data: data + Bytes(repeating: UInt8(pad), count: pad))
    }

    public static func decryptECB(_ data: Bytes) throws -> Bytes {
        let plain = try aes(kCCDecrypt, key: encKey, iv: nil, data: data)
        guard let last = plain.last, last >= 1, last <= 16, Int(last) <= plain.count,
              plain.suffix(Int(last)).allSatisfy({ $0 == last })
        else { throw MideaError.crypto }
        return Bytes(plain[0..<(plain.count - Int(last))])
    }

    /// AES-CBC con IV a cero y sin relleno (capa V3).
    public static func encryptCBC(key: Bytes, _ data: Bytes) throws -> Bytes {
        try aes(kCCEncrypt, key: key, iv: Bytes(repeating: 0, count: 16), data: data)
    }

    public static func decryptCBC(key: Bytes, _ data: Bytes) throws -> Bytes {
        try aes(kCCDecrypt, key: key, iv: Bytes(repeating: 0, count: 16), data: data)
    }

    /// Firma MD5 de los paquetes V2.
    public static func sign(_ data: Bytes) -> Bytes { md5(data + signKey) }

    /// Identificador UDP que la nube usa para buscar el token de un aire.
    public static func udpid(_ deviceID: Bytes) -> Bytes {
        let h = sha256(deviceID)
        return xor(Bytes(h[0..<16]), Bytes(h[16..<32]))
    }

    public static func udpid(deviceID: UInt64, bigEndian: Bool) -> String {
        var le = Bytes()
        for i in 0..<6 { le.append(UInt8(truncatingIfNeeded: deviceID >> (8 * UInt64(i)))) }
        return udpid(bigEndian ? le.reversed() : le).hex
    }
}
