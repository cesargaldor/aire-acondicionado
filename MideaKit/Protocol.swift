// Tramas y paquetes del protocolo local de Midea (port de msmart-ng).

import Foundation

// MARK: - CRC8 y checksum

public enum CRC8 {
    private static let table: Bytes = [
        0x00, 0x5E, 0xBC, 0xE2, 0x61, 0x3F, 0xDD, 0x83, 0xC2, 0x9C, 0x7E, 0x20, 0xA3, 0xFD, 0x1F, 0x41,
        0x9D, 0xC3, 0x21, 0x7F, 0xFC, 0xA2, 0x40, 0x1E, 0x5F, 0x01, 0xE3, 0xBD, 0x3E, 0x60, 0x82, 0xDC,
        0x23, 0x7D, 0x9F, 0xC1, 0x42, 0x1C, 0xFE, 0xA0, 0xE1, 0xBF, 0x5D, 0x03, 0x80, 0xDE, 0x3C, 0x62,
        0xBE, 0xE0, 0x02, 0x5C, 0xDF, 0x81, 0x63, 0x3D, 0x7C, 0x22, 0xC0, 0x9E, 0x1D, 0x43, 0xA1, 0xFF,
        0x46, 0x18, 0xFA, 0xA4, 0x27, 0x79, 0x9B, 0xC5, 0x84, 0xDA, 0x38, 0x66, 0xE5, 0xBB, 0x59, 0x07,
        0xDB, 0x85, 0x67, 0x39, 0xBA, 0xE4, 0x06, 0x58, 0x19, 0x47, 0xA5, 0xFB, 0x78, 0x26, 0xC4, 0x9A,
        0x65, 0x3B, 0xD9, 0x87, 0x04, 0x5A, 0xB8, 0xE6, 0xA7, 0xF9, 0x1B, 0x45, 0xC6, 0x98, 0x7A, 0x24,
        0xF8, 0xA6, 0x44, 0x1A, 0x99, 0xC7, 0x25, 0x7B, 0x3A, 0x64, 0x86, 0xD8, 0x5B, 0x05, 0xE7, 0xB9,
        0x8C, 0xD2, 0x30, 0x6E, 0xED, 0xB3, 0x51, 0x0F, 0x4E, 0x10, 0xF2, 0xAC, 0x2F, 0x71, 0x93, 0xCD,
        0x11, 0x4F, 0xAD, 0xF3, 0x70, 0x2E, 0xCC, 0x92, 0xD3, 0x8D, 0x6F, 0x31, 0xB2, 0xEC, 0x0E, 0x50,
        0xAF, 0xF1, 0x13, 0x4D, 0xCE, 0x90, 0x72, 0x2C, 0x6D, 0x33, 0xD1, 0x8F, 0x0C, 0x52, 0xB0, 0xEE,
        0x32, 0x6C, 0x8E, 0xD0, 0x53, 0x0D, 0xEF, 0xB1, 0xF0, 0xAE, 0x4C, 0x12, 0x91, 0xCF, 0x2D, 0x73,
        0xCA, 0x94, 0x76, 0x28, 0xAB, 0xF5, 0x17, 0x49, 0x08, 0x56, 0xB4, 0xEA, 0x69, 0x37, 0xD5, 0x8B,
        0x57, 0x09, 0xEB, 0xB5, 0x36, 0x68, 0x8A, 0xD4, 0x95, 0xCB, 0x29, 0x77, 0xF4, 0xAA, 0x48, 0x16,
        0xE9, 0xB7, 0x55, 0x0B, 0x88, 0xD6, 0x34, 0x6A, 0x2B, 0x75, 0x97, 0xC9, 0x4A, 0x14, 0xF6, 0xA8,
        0x74, 0x2A, 0xC8, 0x96, 0x15, 0x4B, 0xA9, 0xF7, 0xB6, 0xE8, 0x0A, 0x54, 0xD7, 0x89, 0x6B, 0x35,
    ]

    public static func calculate<S: Sequence>(_ data: S) -> UInt8 where S.Element == UInt8 {
        var crc: UInt8 = 0
        for b in data { crc = table[Int(crc ^ b)] }
        return crc
    }
}

public func frameChecksum<S: Sequence>(_ data: S) -> UInt8 where S.Element == UInt8 {
    var sum = 0
    for b in data { sum += Int(b) }
    return UInt8(truncatingIfNeeded: 0 &- sum)
}

// MARK: - Tramas del aire (0xAA ...)

public enum ACFrame {
    public static let deviceType: UInt8 = 0xAC
    public static let control: UInt8 = 0x02
    public static let query: UInt8 = 0x03

    /// Construye la trama: cabecera de 10 bytes + datos + id de mensaje + CRC + checksum.
    public static func build(frameType: UInt8, body: Bytes, messageID: UInt8) -> Bytes {
        let payload = body + [messageID]
        let data = payload + [CRC8.calculate(payload)]
        var frame = Bytes(repeating: 0, count: 10)
        frame[0] = 0xAA
        frame[1] = UInt8(truncatingIfNeeded: data.count + 10)
        frame[2] = deviceType
        frame[9] = frameType
        frame += data
        frame.append(frameChecksum(frame[1...]))
        return frame
    }

    public static func getState(messageID: UInt8) -> Bytes {
        build(frameType: query, body: [
            0x41, 0x81, 0x00, 0xFF, 0x03, 0xFF, 0x00,
            0x02,  // temperatura interior
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x03,
        ], messageID: messageID)
    }

    public static func setState(_ s: ACSettings, messageID: UInt8) -> Bytes {
        let integral = Int(s.target.rounded(.towardZero))
        let fractional = s.target - Double(integral)
        var temperature: UInt8 = 0
        var temperatureAlt: UInt8 = 0
        if (17...30).contains(integral) {
            temperature = UInt8((integral - 16) & 0xF)
        } else {
            temperatureAlt = UInt8((integral - 12) & 0x1F)
        }
        if fractional > 0 { temperature |= 0x10 }

        let mode: UInt8 = (s.mode & 0x7) << 5
        let swing: UInt8 = 0x30 | (s.swing & 0x3F)
        var power: UInt8 = 0x02  // origen: app
        if s.beep { power |= 0x40 }
        if s.power { power |= 0x01 }
        var turboAlt: UInt8 = 0
        if s.turbo { turboAlt |= 0x20 }
        if s.followMe { turboAlt |= 0x80 }
        var eco: UInt8 = 0
        if s.eco { eco |= 0x80 }
        if s.purifier { eco |= 0x20 }
        if s.auxHeat { eco |= 0x08 }
        var flags: UInt8 = 0
        if s.sleep { flags |= 0x01 }
        if s.turbo { flags |= 0x02 }
        if s.fahrenheit { flags |= 0x04 }
        let freeze: UInt8 = s.freezeProtection ? 0x80 : 0
        let independent: UInt8 = s.independentAuxHeat ? 0x08 : 0
        let humidity: UInt8 = s.humidity & 0x7F
        let tempMode: UInt8 = temperature | mode

        var body: Bytes = [0x40, power, tempMode, s.fan, 0x7F, 0x7F, 0x00, swing, turboAlt, eco, flags]
        body += Bytes(repeating: 0, count: 7)
        body += [temperatureAlt, humidity, 0x00, freeze, independent, 0x00]
        return build(frameType: control, body: body, messageID: messageID)
    }

    /// Enciende o apaga la pantalla del aire (es un conmutador: invierte su estado).
    public static func toggleDisplay(beep: Bool, messageID: UInt8) -> Bytes {
        var body = Bytes(repeating: 0, count: 21)
        body[0] = 0x41
        body[1] = 0x02 | (beep ? 0x40 : 0)
        body[3] = 0xFF
        body[4] = 0x02
        body[6] = 0x02
        return build(frameType: query, body: body, messageID: messageID)
    }

    /// Valida una trama recibida y devuelve su carga útil (sin CRC ni checksum) y el id de respuesta.
    public static func payload(of frame: Bytes) throws -> Bytes {
        guard frame.count >= 13, frame[0] == 0xAA else {
            throw MideaError.protocolError("trama demasiado corta")
        }
        guard frameChecksum(frame[1..<(frame.count - 1)]) == frame[frame.count - 1] else {
            throw MideaError.protocolError("checksum de trama incorrecto")
        }
        guard frame[2] == deviceType else {
            throw MideaError.protocolError("tipo de aparato inesperado")
        }
        let body = Bytes(frame[10..<(frame.count - 1)])  // carga + CRC
        let crc = CRC8.calculate(body.dropLast())
        let sum = frameChecksum(body.dropLast())
        guard body.last == crc || body.last == sum else {
            throw MideaError.protocolError("CRC de carga incorrecto")
        }
        return Bytes(body.dropLast())  // sin CRC
    }
}

// MARK: - Estado

/// Lo que se envía al aire. Se rellena a partir del último estado para no tocar lo que no cambia.
public struct ACSettings: Equatable {
    public var power = false
    public var target = 25.0
    public var mode: UInt8 = 2
    public var fan: UInt8 = 102
    public var swing: UInt8 = 0
    public var eco = false
    public var turbo = false
    public var sleep = false
    public var fahrenheit = false
    public var followMe = false
    public var purifier = false
    public var humidity: UInt8 = 40
    public var freezeProtection = false
    public var auxHeat = false
    public var independentAuxHeat = false
    public var beep = false

    public init() {}
}

public struct ACStatus: Equatable {
    public var power = false
    public var target = 25.0
    public var mode: UInt8 = 0
    public var fan: UInt8 = 102
    public var swing: UInt8 = 0
    public var eco = false
    public var turbo = false
    public var sleep = false
    public var fahrenheit = false
    public var followMe = false
    public var purifier = false
    public var auxHeat = false
    public var independentAuxHeat = false
    public var indoor: Double?
    public var outdoor: Double?
    public var humidity: UInt8?
    public var freezeProtection: Bool?
    public var displayOn = true
    public var filterAlert = false
    public var errorCode: UInt8 = 0

    /// Interpreta la carga de una respuesta de estado (id 0xC0).
    public init?(payload p: Bytes) {
        guard p.count >= 17, p[0] == 0xC0 else { return nil }
        power = p[1] & 0x01 != 0
        let half: Double = (p[2] & 0x10) != 0 ? 0.5 : 0
        target = Double(p[2] & 0xF) + 16 + half
        mode = (p[2] >> 5) & 0x7
        fan = p[3] & 0x7F
        swing = p[7] & 0xF
        turbo = p[8] & 0x20 != 0
        independentAuxHeat = p[8] & 0x40 != 0
        followMe = p[8] & 0x80 != 0
        eco = p[9] & 0x10 != 0
        purifier = p[9] & 0x20 != 0
        auxHeat = p[9] & 0x08 != 0
        sleep = p[10] & 0x01 != 0
        turbo = turbo || p[10] & 0x02 != 0
        fahrenheit = p[10] & 0x04 != 0
        indoor = Self.temperature(p[11], decimals: Double(p[15] & 0xF) / 10, fahrenheit: fahrenheit)
        outdoor = Self.temperature(p[12], decimals: Double(p[15] >> 4) / 10, fahrenheit: fahrenheit)
        let alt = p[13] & 0x1F
        if alt != 0 {
            target = Double(alt) + 12 + half
        }
        filterAlert = p[13] & 0x20 != 0
        displayOn = p[14] != 0x70
        errorCode = p[16]
        if p.count >= 20 { humidity = p[19] & 0x7F }
        if p.count >= 22 { freezeProtection = p[21] & 0x80 != 0 }
    }

    private static func temperature(_ data: UInt8, decimals: Double, fahrenheit: Bool) -> Double? {
        if data == 0xFF { return nil }
        let t = (Double(data) - 50) / 2
        let whole = t.rounded(.towardZero)
        if !fahrenheit && decimals != 0 { return whole + (t >= 0 ? decimals : -decimals) }
        if decimals >= 0.5 { return whole + (t >= 0 ? 0.5 : -0.5) }
        return t
    }

    /// Ajustes para mandar al aire conservando todo lo que no se cambia.
    public var settings: ACSettings {
        var s = ACSettings()
        s.power = power
        s.target = target
        s.mode = mode == 0 ? 2 : mode
        s.fan = fan
        s.swing = swing
        s.eco = eco
        s.turbo = turbo
        s.sleep = sleep
        s.fahrenheit = fahrenheit
        s.followMe = followMe
        s.purifier = purifier
        s.humidity = humidity ?? 40
        s.freezeProtection = freezeProtection ?? false
        s.auxHeat = auxHeat
        s.independentAuxHeat = independentAuxHeat
        return s
    }
}

// MARK: - Paquete V2 (0x5A5A): envuelve las tramas cifradas

public enum V2Packet {
    public static func timestamp(_ date: Date = Date()) -> Bytes {
        var cal = Calendar(identifier: .gregorian)
        cal.timeZone = TimeZone(identifier: "UTC")!
        let c = cal.dateComponents([.year, .month, .day, .hour, .minute, .second, .nanosecond], from: date)
        let year = c.year ?? 2000
        return [
            UInt8((c.nanosecond ?? 0) / 10_000_000),
            UInt8(c.second ?? 0), UInt8(c.minute ?? 0), UInt8(c.hour ?? 0),
            UInt8(c.day ?? 1), UInt8(c.month ?? 1),
            UInt8(year % 100), UInt8(year / 100),
        ]
    }

    public static func encode(deviceID: UInt64, frame: Bytes, timestamp ts: Bytes? = nil) throws -> Bytes {
        let encrypted = try MideaCrypto.encryptECB(frame)
        let length = 40 + encrypted.count + 16
        var header: Bytes = [0x5A, 0x5A, 0x01, 0x11,
                             UInt8(length & 0xFF), UInt8(length >> 8),
                             0x20, 0x00, 0, 0, 0, 0]
        header += ts ?? timestamp()
        for i in 0..<8 { header.append(UInt8(truncatingIfNeeded: deviceID >> (8 * UInt64(i)))) }
        header += Bytes(repeating: 0, count: 12)
        let packet = header + encrypted
        return packet + MideaCrypto.sign(packet)
    }

    public static func decode(_ data: Bytes) throws -> Bytes {
        guard data.count >= 56, data[0] == 0x5A, data[1] == 0x5A else {
            throw MideaError.protocolError("paquete V2 no reconocido")
        }
        let length = Int(data[4]) | Int(data[5]) << 8
        guard length >= 56, data.count >= length else {
            throw MideaError.protocolError("paquete V2 truncado")
        }
        let packet = Bytes(data[0..<length])
        let hash = Bytes(packet[(length - 16)...])
        guard MideaCrypto.sign(Bytes(packet[0..<(length - 16)])) == hash else {
            throw MideaError.protocolError("firma MD5 incorrecta")
        }
        return try MideaCrypto.decryptECB(Bytes(packet[40..<(length - 16)]))
    }
}

// MARK: - Capa V3 (0x8370): handshake y cifrado de sesión

public enum V3Packet {
    static let handshakeRequest: UInt8 = 0x0
    static let handshakeResponse: UInt8 = 0x1
    static let encryptedResponse: UInt8 = 0x3
    static let encryptedRequest: UInt8 = 0x6
    static let error: UInt8 = 0xF

    static func header(length: Int, extra: UInt8) -> Bytes {
        [0x83, 0x70, UInt8((length >> 8) & 0xFF), UInt8(length & 0xFF), 0x20, extra]
    }

    public static func handshake(packetID: UInt16, token: Bytes) -> Bytes {
        header(length: token.count, extra: handshakeRequest)
            + [UInt8(packetID >> 8), UInt8(packetID & 0xFF)] + token
    }

    public static func encrypt(packetID: UInt16, data: Bytes, localKey: Bytes, padding: Bytes? = nil) throws -> Bytes {
        let remainder = (data.count + 2) % 16
        let pad = remainder == 0 ? 0 : 16 - remainder
        let h = header(length: data.count + pad + 32, extra: UInt8(pad << 4) | encryptedRequest)
        let filler = padding.map { Bytes($0.prefix(pad)) } ?? MideaCrypto.random(pad)
        let payload = [UInt8(packetID >> 8), UInt8(packetID & 0xFF)] + data + filler
        let hash = MideaCrypto.sha256(h + payload)
        return h + (try MideaCrypto.encryptCBC(key: localKey, payload)) + hash
    }

    /// Extrae el siguiente paquete completo del búfer, si lo hay.
    public static func next(from buffer: inout Bytes) -> Bytes? {
        guard let start = (0..<max(0, buffer.count - 1)).first(where: { buffer[$0] == 0x83 && buffer[$0 + 1] == 0x70 }) else {
            if buffer.count > 1 { buffer.removeFirst(buffer.count - 1) }
            return nil
        }
        if start > 0 { buffer.removeFirst(start) }
        guard buffer.count >= 6 else { return nil }
        let total = (Int(buffer[2]) << 8 | Int(buffer[3])) + 8
        guard buffer.count >= total else { return nil }
        let packet = Bytes(buffer[0..<total])
        buffer.removeFirst(total)
        return packet
    }

    public enum Decoded {
        case handshake(Bytes)
        case data(Bytes)
    }

    public static func decode(_ packet: Bytes, localKey: Bytes?) throws -> Decoded {
        guard packet.count >= 8, packet[0] == 0x83, packet[1] == 0x70 else {
            throw MideaError.protocolError("inicio de paquete V3 incorrecto")
        }
        guard packet[4] == 0x20 else { throw MideaError.protocolError("byte mágico incorrecto") }
        switch packet[5] & 0xF {
        case handshakeResponse:
            return .handshake(Bytes(packet[8...]))
        case encryptedResponse:
            guard let key = localKey, packet.count >= 6 + 32 + 16 else {
                throw MideaError.protocolError("respuesta cifrada sin sesión")
            }
            let h = Bytes(packet[0..<6])
            let body = Bytes(packet[6..<(packet.count - 32)])
            let hash = Bytes(packet[(packet.count - 32)...])
            let plain = try MideaCrypto.decryptCBC(key: key, body)
            guard MideaCrypto.sha256(h + plain) == hash else {
                throw MideaError.protocolError("SHA256 de respuesta incorrecto")
            }
            let pad = Int(h[5] >> 4)
            guard plain.count >= 2 + pad else { throw MideaError.protocolError("respuesta vacía") }
            return .data(Bytes(plain[2..<(plain.count - pad)]))
        case error:
            throw MideaError.protocolError("el aire devolvió un paquete de error")
        default:
            throw MideaError.protocolError("tipo de paquete inesperado")
        }
    }

    /// Calcula la clave de sesión a partir de la respuesta del handshake.
    public static func localKey(key: Bytes, handshakeData data: Bytes) throws -> Bytes {
        guard data.count == 64 else { throw MideaError.authentication("respuesta de handshake inválida") }
        let plain = try MideaCrypto.decryptCBC(key: key, Bytes(data[0..<32]))
        guard MideaCrypto.sha256(plain) == Bytes(data[32..<64]) else {
            throw MideaError.authentication("el token o la key no son de este aire")
        }
        return MideaCrypto.xor(plain, key)
    }
}

// MARK: - Descubrimiento

public struct DiscoveredDevice: Equatable, Codable {
    public var ip: String
    public var port: Int
    public var id: UInt64
    public var name: String
    public var sn: String
    public var type: Int
    public var version: Int
}

public enum Discovery {
    public static let message: Bytes = [
        0x5a, 0x5a, 0x01, 0x11, 0x48, 0x00, 0x92, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x7f, 0x75, 0xbd, 0x6b, 0x3e, 0x4f, 0x8b, 0x76,
        0x2e, 0x84, 0x9c, 0x6e, 0x57, 0x8d, 0x65, 0x90, 0x03, 0x6e, 0x9d, 0x43, 0x42, 0xa5, 0x0f, 0x1f,
        0x56, 0x9e, 0xb8, 0xec, 0x91, 0x8e, 0x92, 0xe5,
    ]

    /// Interpreta la respuesta de un aire al mensaje de descubrimiento.
    public static func parse(_ response: Bytes, from ip: String) throws -> DiscoveredDevice {
        guard response.count >= 2 else { throw MideaError.protocolError("respuesta vacía") }
        let version: Int
        var data = response
        if response[0] == 0x5A && response[1] == 0x5A {
            version = 2
        } else if response[0] == 0x83 && response[1] == 0x70 {
            version = 3
            guard response.count > 24 else { throw MideaError.protocolError("respuesta corta") }
            data = Bytes(response[8..<(response.count - 16)])
        } else {
            throw MideaError.protocolError("aparato no compatible (versión 1)")
        }
        guard data.count > 56 else { throw MideaError.protocolError("respuesta corta") }
        var id: UInt64 = 0
        for i in 0..<6 { id |= UInt64(data[20 + i]) << (8 * UInt64(i)) }
        let plain = try MideaCrypto.decryptECB(Bytes(data[40..<(data.count - 16)]))
        guard plain.count > 41 else { throw MideaError.protocolError("datos de descubrimiento cortos") }
        let port = Int(plain[4]) | Int(plain[5]) << 8
        let sn = String(decoding: plain[8..<40], as: UTF8.self)
        let nameLength = Int(plain[40])
        guard plain.count >= 41 + nameLength else { throw MideaError.protocolError("nombre truncado") }
        let name = String(decoding: plain[41..<(41 + nameLength)], as: UTF8.self)
        let parts = name.split(separator: "_")
        let type = parts.count > 1 ? Int(parts[1], radix: 16) ?? 0 : 0
        return DiscoveredDevice(ip: ip, port: port, id: id, name: name, sn: sn, type: type, version: version)
    }
}
