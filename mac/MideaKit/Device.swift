// Un aire Midea en la red local: conexión, autenticación V3, consulta y control.

import Foundation

public struct DeviceConfig: Codable, Equatable {
    public var ip: String
    public var port: Int
    public var id: UInt64
    public var version: Int
    public var token: String?
    public var key: String?
    public var name: String?

    public init(ip: String, port: Int = 6444, id: UInt64, version: Int = 3,
                token: String? = nil, key: String? = nil, name: String? = nil) {
        self.ip = ip
        self.port = port
        self.id = id
        self.version = version
        self.token = token
        self.key = key
        self.name = name
    }

    public init(_ d: DiscoveredDevice) {
        self.init(ip: d.ip, port: d.port, id: d.id, version: d.version, name: d.name)
    }

    public var needsKeys: Bool { version == 3 && (token == nil || key == nil) }
}

public final class MideaAC {
    public private(set) var config: DeviceConfig
    public private(set) var lastStatus: ACStatus?

    private let queue = DispatchQueue(label: "aire.device")
    private let conn = TCPConnection()
    private var localKey: Bytes?
    private var authExpiry = Date.distantPast
    private var packetID: UInt16 = 0
    private var messageID: UInt8 = 0
    private var buffer = Bytes()

    /// Si se activa, imprime el tráfico en la consola (para depurar).
    public var debug = false

    public init(config: DeviceConfig) {
        self.config = config
    }

    // MARK: API asíncrona

    public func refresh() async throws -> ACStatus {
        try await run { try self.perform { ACFrame.getState(messageID: self.nextMessageID()) } }
    }

    public func apply(_ settings: ACSettings) async throws -> ACStatus {
        try await run { try self.perform { ACFrame.setState(settings, messageID: self.nextMessageID()) } }
    }

    /// Enciende o apaga la pantalla del aire y devuelve el estado nuevo.
    public func toggleDisplay(beep: Bool) async throws -> ACStatus {
        try await run {
            // Es un conmutador: se envía una sola vez, sin reintentos, para no invertirlo dos veces.
            try self.sendOnce(ACFrame.toggleDisplay(beep: beep, messageID: self.nextMessageID()))
            return try self.perform { ACFrame.getState(messageID: self.nextMessageID()) }
        }
    }

    /// Comprueba que el token y la key funcionan con este aire.
    public func authenticate() async throws {
        try await run {
            self.disconnect()
            try self.ensureSession()
        }
    }

    public func updateIP(_ ip: String, port: Int) {
        queue.sync {
            config.ip = ip
            config.port = port
            disconnect()
        }
    }

    public func close() {
        queue.async { self.disconnect() }
    }

    private func run<T>(_ work: @escaping () throws -> T) async throws -> T {
        try await withCheckedThrowingContinuation { cont in
            queue.async {
                cont.resume(with: Result { try work() })
            }
        }
    }

    // MARK: Sesión (siempre en `queue`)

    private func log(_ s: @autoclosure () -> String) {
        if debug { print("[aire] \(s())") }
    }

    private func nextMessageID() -> UInt8 {
        messageID &+= 1
        return messageID
    }

    private func nextPacketID() -> UInt16 {
        let id = packetID
        packetID = (packetID + 1) & 0xFFF
        return id
    }

    private func disconnect() {
        conn.close()
        localKey = nil
        buffer = []
    }

    private func ensureSession() throws {
        if !conn.isOpen {
            disconnect()
            log("conectando con \(config.ip):\(config.port)")
            try conn.connect(ip: config.ip, port: config.port)
            packetID = 0
        }
        if config.version == 3 && (localKey == nil || Date() > authExpiry) {
            try handshake()
        }
    }

    private func handshake() throws {
        guard let token = config.token.flatMap({ Bytes(hex: $0) }),
              let key = config.key.flatMap({ Bytes(hex: $0) })
        else { throw MideaError.authentication("faltan el token y la key") }

        buffer = []
        for _ in 0..<3 {
            let packet = V3Packet.handshake(packetID: nextPacketID(), token: token)
            log("handshake → \(packet.hex)")
            try conn.send(packet)
            let deadline = Date().addingTimeInterval(3)
            while let response = try readPacket(until: deadline) {
                log("handshake ← \(response.hex)")
                do {
                    if case .handshake(let data) = try V3Packet.decode(response, localKey: nil) {
                        localKey = try V3Packet.localKey(key: key, handshakeData: data)
                        authExpiry = Date().addingTimeInterval(11 * 3600)
                        log("autenticado")
                        Thread.sleep(forTimeInterval: 1)  // el aire necesita un respiro
                        return
                    }
                } catch {
                    disconnect()
                    throw MideaError.authentication("\(error)")
                }
            }
        }
        disconnect()
        throw MideaError.timeout
    }

    /// Devuelve el siguiente paquete completo o nil si se agota el tiempo.
    private func readPacket(until deadline: Date) throws -> Bytes? {
        while true {
            if let p = extractPacket() { return p }
            let remaining = deadline.timeIntervalSinceNow
            if remaining <= 0 { return nil }
            let chunk = try conn.receive(timeout: remaining)
            if chunk.isEmpty { return nil }
            buffer += chunk
        }
    }

    private func extractPacket() -> Bytes? {
        if config.version == 3 { return V3Packet.next(from: &buffer) }
        // V2: 5A5A + longitud en little endian
        guard let start = (0..<max(0, buffer.count - 1)).first(where: { buffer[$0] == 0x5A && buffer[$0 + 1] == 0x5A }) else {
            return nil
        }
        if start > 0 { buffer.removeFirst(start) }
        guard buffer.count >= 6 else { return nil }
        let length = Int(buffer[4]) | Int(buffer[5]) << 8
        guard length >= 6, buffer.count >= length else { return nil }
        let p = Bytes(buffer[0..<length])
        buffer.removeFirst(length)
        return p
    }

    private func frame(from packet: Bytes) throws -> Bytes {
        if config.version == 3 {
            guard case .data(let inner) = try V3Packet.decode(packet, localKey: localKey) else {
                throw MideaError.protocolError("paquete inesperado")
            }
            return try V2Packet.decode(inner)
        }
        return try V2Packet.decode(packet)
    }

    private func send(_ cmd: Bytes) throws {
        log("trama → \(cmd.hex)")
        let packet = try V2Packet.encode(deviceID: config.id, frame: cmd)
        if config.version == 3 {
            guard let key = localKey else { throw MideaError.authentication("sin sesión") }
            try conn.send(try V3Packet.encrypt(packetID: nextPacketID(), data: packet, localKey: key))
        } else {
            try conn.send(packet)
        }
    }

    /// Envía una trama sin esperar estado y descarta lo que conteste.
    private func sendOnce(_ cmd: Bytes) throws {
        try ensureSession()
        try send(cmd)
        let deadline = Date().addingTimeInterval(1)
        while let response = try readPacket(until: deadline) {
            if let f = try? frame(from: response) { log("trama ← \(f.hex) (descartada)") }
        }
    }

    private func exchange(_ makeFrame: () -> Bytes) throws -> ACStatus {
        try ensureSession()

        // Descarta respuestas antiguas que se hayan quedado en el búfer
        if let chunk = try? conn.receive(timeout: 0.05), !chunk.isEmpty { buffer += chunk }
        while extractPacket() != nil {}
        try ensureSession()  // por si la lectura anterior detectó la conexión cerrada

        for _ in 0..<3 {
            try send(makeFrame())

            let deadline = Date().addingTimeInterval(3)
            while let response = try readPacket(until: deadline) {
                do {
                    let f = try frame(from: response)
                    log("trama ← \(f.hex)")
                    let payload = try ACFrame.payload(of: f)
                    if let status = ACStatus(payload: payload) {
                        lastStatus = status
                        return status
                    }
                } catch {
                    log("respuesta ignorada: \(error)")
                }
            }
            log("sin respuesta, reintentando")
        }
        disconnect()
        throw MideaError.timeout
    }

    /// Ejecuta un intercambio y, si la conexión se había caído, reconecta una vez.
    private func perform(_ makeFrame: () -> Bytes) throws -> ACStatus {
        do {
            return try exchange(makeFrame)
        } catch MideaError.authentication(let m) {
            throw MideaError.authentication(m)
        } catch {
            log("reintentando tras error: \(error)")
            disconnect()
            return try exchange(makeFrame)
        }
    }
}

// MARK: - Configuración inicial

public enum Setup {
    /// Busca aires en la red local.
    public static func discover(target: String = "255.255.255.255", timeout: Double = 4) async throws -> [DiscoveredDevice] {
        try await withCheckedThrowingContinuation { cont in
            DispatchQueue.global().async {
                cont.resume(with: Result { try UDPDiscovery.scan(target: target, timeout: timeout) })
            }
        }
    }

    /// Inicia sesión en SmartHome, obtiene el token y la key del aire y comprueba que funcionan.
    /// La contraseña solo se usa aquí y no se guarda.
    public static func obtainKeys(for device: DiscoveredDevice, account: String, password: String) async throws -> DeviceConfig {
        let cloud = SmartHomeCloud()
        try await cloud.login(account: account, password: password)

        var lastError: Error = MideaError.authentication("la cuenta no tiene este aire")
        for bigEndian in [false, true] {
            let udpid = MideaCrypto.udpid(deviceID: device.id, bigEndian: bigEndian)
            do {
                let (token, key) = try await cloud.token(udpid: udpid, applianceID: device.id)
                var config = DeviceConfig(device)
                config.token = token
                config.key = key
                let ac = MideaAC(config: config)
                defer { ac.close() }
                try await ac.authenticate()
                return config
            } catch {
                lastError = error
            }
        }
        throw lastError
    }
}
