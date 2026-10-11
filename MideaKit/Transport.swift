// Sockets: difusión UDP para encontrar el aire y TCP para hablar con él.
// Son bloqueantes a propósito: se usan siempre desde una cola en segundo plano.

import Darwin
import Foundation

private func ipv4Address(_ ip: String, port: UInt16) -> sockaddr_in? {
    var addr = sockaddr_in()
    addr.sin_len = UInt8(MemoryLayout<sockaddr_in>.size)
    addr.sin_family = sa_family_t(AF_INET)
    addr.sin_port = port.bigEndian
    guard inet_pton(AF_INET, ip, &addr.sin_addr) == 1 else { return nil }
    return addr
}

private func setTimeout(_ fd: Int32, _ option: Int32, seconds: Double) {
    var tv = timeval(tv_sec: Int(seconds), tv_usec: Int32((seconds - Double(Int(seconds))) * 1_000_000))
    setsockopt(fd, SOL_SOCKET, option, &tv, socklen_t(MemoryLayout<timeval>.size))
}

private func errnoText() -> String { String(cString: strerror(errno)) }

// MARK: - Descubrimiento UDP

public enum UDPDiscovery {
    /// Envía el mensaje de descubrimiento (a toda la red o a una IP) y recoge las respuestas.
    public static func scan(target: String = "255.255.255.255", timeout: Double = 4) throws -> [DiscoveredDevice] {
        let fd = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP)
        guard fd >= 0 else { throw MideaError.network("no se pudo abrir el socket UDP: \(errnoText())") }
        defer { close(fd) }

        var yes: Int32 = 1
        setsockopt(fd, SOL_SOCKET, SO_BROADCAST, &yes, socklen_t(MemoryLayout<Int32>.size))
        setTimeout(fd, SO_RCVTIMEO, seconds: 0.5)

        for port: UInt16 in [6445, 20086] {
            guard var addr = ipv4Address(target, port: port) else { throw MideaError.network("IP no válida") }
            for _ in 0..<3 {
                _ = Discovery.message.withUnsafeBytes { buf in
                    withUnsafePointer(to: &addr) { ptr in
                        ptr.withMemoryRebound(to: sockaddr.self, capacity: 1) {
                            sendto(fd, buf.baseAddress, buf.count, 0, $0, socklen_t(MemoryLayout<sockaddr_in>.size))
                        }
                    }
                }
            }
        }

        var found: [String: DiscoveredDevice] = [:]
        let deadline = Date().addingTimeInterval(timeout)
        var buffer = Bytes(repeating: 0, count: 2048)
        let capacity = buffer.count
        while Date() < deadline {
            var from = sockaddr_in()
            var fromLen = socklen_t(MemoryLayout<sockaddr_in>.size)
            let n = withUnsafeMutablePointer(to: &from) { ptr in
                ptr.withMemoryRebound(to: sockaddr.self, capacity: 1) {
                    recvfrom(fd, &buffer, capacity, 0, $0, &fromLen)
                }
            }
            guard n > 0 else { continue }
            var ipBuf = [CChar](repeating: 0, count: Int(INET_ADDRSTRLEN))
            var inAddr = from.sin_addr
            inet_ntop(AF_INET, &inAddr, &ipBuf, socklen_t(INET_ADDRSTRLEN))
            let ip = String(cString: ipBuf)
            if found[ip] != nil { continue }
            if let device = try? Discovery.parse(Bytes(buffer[0..<n]), from: ip) {
                found[ip] = device
                if target != "255.255.255.255" { break }
            }
        }
        return Array(found.values).sorted { $0.ip < $1.ip }
    }
}

// MARK: - TCP

public final class TCPConnection {
    private var fd: Int32 = -1

    public init() {}
    deinit { close() }

    public var isOpen: Bool { fd >= 0 }

    public func connect(ip: String, port: Int, timeout: Double = 5) throws {
        close()
        guard var addr = ipv4Address(ip, port: UInt16(port)) else { throw MideaError.network("IP no válida: \(ip)") }
        let s = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP)
        guard s >= 0 else { throw MideaError.network("no se pudo abrir el socket: \(errnoText())") }

        var noSigPipe: Int32 = 1
        setsockopt(s, SOL_SOCKET, SO_NOSIGPIPE, &noSigPipe, socklen_t(MemoryLayout<Int32>.size))

        // Conexión no bloqueante para poder poner un límite de tiempo
        let flags = fcntl(s, F_GETFL, 0)
        _ = fcntl(s, F_SETFL, flags | O_NONBLOCK)
        let result = withUnsafePointer(to: &addr) { ptr in
            ptr.withMemoryRebound(to: sockaddr.self, capacity: 1) {
                Darwin.connect(s, $0, socklen_t(MemoryLayout<sockaddr_in>.size))
            }
        }
        if result != 0 {
            guard errno == EINPROGRESS else {
                let msg = errnoText()
                Darwin.close(s)
                throw MideaError.network("no se pudo conectar con \(ip): \(msg)")
            }
            var pfd = pollfd(fd: s, events: Int16(POLLOUT), revents: 0)
            let ready = poll(&pfd, 1, Int32(timeout * 1000))
            var err: Int32 = 0
            var len = socklen_t(MemoryLayout<Int32>.size)
            getsockopt(s, SOL_SOCKET, SO_ERROR, &err, &len)
            guard ready > 0, err == 0 else {
                Darwin.close(s)
                throw ready == 0 ? MideaError.timeout : MideaError.network("no se pudo conectar con \(ip)")
            }
        }
        _ = fcntl(s, F_SETFL, flags & ~O_NONBLOCK)
        fd = s
    }

    public func send(_ data: Bytes) throws {
        guard fd >= 0 else { throw MideaError.network("sin conexión") }
        var sent = 0
        while sent < data.count {
            let n = data.withUnsafeBytes { buf in
                Darwin.send(fd, buf.baseAddress! + sent, data.count - sent, 0)
            }
            guard n > 0 else {
                close()
                throw MideaError.network("conexión cerrada al enviar")
            }
            sent += n
        }
    }

    /// Lee lo que llegue en como mucho `timeout` segundos. Devuelve vacío si no llega nada.
    public func receive(timeout: Double) throws -> Bytes {
        guard fd >= 0 else { throw MideaError.network("sin conexión") }
        var pfd = pollfd(fd: fd, events: Int16(POLLIN), revents: 0)
        let ready = poll(&pfd, 1, Int32(max(0, timeout) * 1000))
        if ready == 0 { return [] }
        var buffer = Bytes(repeating: 0, count: 4096)
        let capacity = buffer.count
        let n = Darwin.recv(fd, &buffer, capacity, 0)
        guard n > 0 else {
            close()
            throw MideaError.network("el aire cerró la conexión")
        }
        return Bytes(buffer[0..<n])
    }

    public func close() {
        if fd >= 0 { Darwin.close(fd) }
        fd = -1
    }
}
