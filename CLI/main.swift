// aire-cli: herramienta para probar el núcleo sin la interfaz.
//
//   ./aire-cli selftest Tests/vectors.json   compara con msmart-ng (no toca el aire)
//   ./aire-cli discover                      busca aires en la red
//   ./aire-cli state config.json             lee el estado usando un config.json (ip, id, token, key)
//   ./aire-cli set config.json power=on temp=23 mode=cool fan=auto
//   ./aire-cli login                         obtiene token y key con tu cuenta SmartHome

import Foundation

let args = CommandLine.arguments
func usage() -> Never {
    print("""
    Uso:
      aire-cli selftest <vectors.json>
      aire-cli discover
      aire-cli state <config.json>
      aire-cli set <config.json> power=on|off temp=23 mode=cool|heat|dry|fan|auto
                                 fan=auto|silent|low|medium|high|max swing=off|vertical|horizontal|both
                                 eco=on|off sleep=on|off turbo=on|off beep=on|off
      aire-cli display <config.json>             enciende o apaga la pantalla del aire
      aire-cli login
    """)
    exit(2)
}

func describe(_ s: ACStatus) -> String {
    let mode = ACMode(rawValue: s.mode)?.name ?? "modo \(s.mode)"
    let indoor = s.indoor.map { String(format: "%.1f°", $0) } ?? "?"
    let outdoor = s.outdoor.map { String(format: "%.1f°", $0) } ?? "?"
    var extras = [SwingMode(raw: s.swing) == .off ? nil : "oscila \(SwingMode(raw: s.swing).name.lowercased())",
                  s.eco ? "eco" : nil, s.sleep ? "sleep" : nil, s.turbo ? "turbo" : nil,
                  s.displayOn ? nil : "pantalla apagada", s.filterAlert ? "LIMPIAR FILTRO" : nil].compactMap { $0 }
    if s.errorCode != 0 { extras.append("error \(s.errorCode)") }
    return "\(s.power ? "ENCENDIDO" : "apagado") · \(mode) · objetivo \(s.target)° · ventilador \(FanSpeed(raw: s.fan).name) (\(s.fan)) · dentro \(indoor) · fuera \(outdoor)"
        + (extras.isEmpty ? "" : " · " + extras.joined(separator: " · "))
}

func isOn(_ s: String) -> Bool {
    ["on", "1", "si", "sí", "true", "yes"].contains(s.lowercased())
}

func loadConfig(_ path: String) -> DeviceConfig {
    guard let data = FileManager.default.contents(atPath: path) else {
        print("No se puede leer \(path)")
        exit(1)
    }
    if let c = try? JSONDecoder().decode(DeviceConfig.self, from: data) { return c }
    if let c = ConfigStore.importPythonConfig(data) { return c }
    print("Formato de \(path) no reconocido")
    exit(1)
}

// MARK: - Autocomprobación

func selftest(_ path: String) -> Int {
    guard let data = FileManager.default.contents(atPath: path),
          let v = try? JSONSerialization.jsonObject(with: data) as? [String: Any]
    else {
        print("No se pueden leer los vectores en \(path)")
        return 1
    }
    var failures = 0
    func check(_ name: String, _ ok: Bool, _ detail: @autoclosure () -> String = "") {
        print((ok ? "  OK     " : "  FALLO  ") + name + (ok ? "" : "  " + detail()))
        if !ok { failures += 1 }
    }
    func g(_ section: String) -> [String: Any] { v[section] as? [String: Any] ?? [:] }
    func b(_ d: [String: Any], _ k: String) -> Bytes { Bytes(hex: d[k] as? String ?? "") ?? [] }
    func n(_ d: [String: Any], _ k: String) -> Double { (d[k] as? NSNumber)?.doubleValue ?? .nan }
    func u(_ d: [String: Any], _ k: String) -> UInt64 { (d[k] as? NSNumber)?.uint64Value ?? 0 }
    func t(_ d: [String: Any], _ k: String) -> Bool { (d[k] as? NSNumber)?.boolValue ?? false }
    func s(_ d: [String: Any], _ k: String) -> String { d[k] as? String ?? "" }
    func run(_ name: String, _ body: () throws -> Void) {
        do { try body() } catch { check(name, false, "error: \(error)") }
    }

    print("Comparando con msmart-ng:")

    let crc = g("crc8")
    check("CRC8", CRC8.calculate(b(crc, "input")) == UInt8(n(crc, "crc")))
    check("checksum", frameChecksum(b(crc, "input")) == UInt8(n(crc, "checksum")))

    let gs = g("get_state")
    let getState = ACFrame.getState(messageID: UInt8(n(gs, "message_id")))
    check("trama de consulta", getState == b(gs, "frame"), getState.hex)

    for section in ["set_state", "set_state_low", "set_state_extras"] {
        let d = g(section)
        var st = ACSettings()
        st.beep = t(d, "beep")
        st.eco = t(d, "eco")
        st.sleep = t(d, "sleep")
        st.turbo = t(d, "turbo")
        st.power = t(d, "power")
        st.target = n(d, "target")
        st.mode = UInt8(n(d, "mode"))
        st.fan = UInt8(n(d, "fan"))
        st.swing = UInt8(n(d, "swing"))
        st.humidity = UInt8(n(d, "humidity"))
        let frame = ACFrame.setState(st, messageID: UInt8(n(d, "message_id")))
        check("trama de control (\(section))", frame == b(d, "frame"), frame.hex)
    }

    for section in ["toggle_display", "toggle_display_silent"] {
        let d = g(section)
        let frame = ACFrame.toggleDisplay(beep: t(d, "beep"), messageID: UInt8(n(d, "message_id")))
        check("trama de pantalla (\(section))", frame == b(d, "frame"), frame.hex)
    }

    run("paquete V2") {
        let d = g("v2_packet")
        let packet = try V2Packet.encode(deviceID: u(d, "device_id"), frame: b(d, "frame"), timestamp: b(d, "timestamp"))
        check("paquete V2 (cifrado + firma)", packet == b(d, "packet"), packet.hex)
        check("paquete V2 (descifrado)", try V2Packet.decode(b(d, "packet")) == b(d, "decoded"))
    }

    run("V3") {
        let r = g("v3_request")
        let enc = try V3Packet.encrypt(packetID: UInt16(n(r, "packet_id")), data: b(r, "data"),
                                       localKey: b(r, "local_key"), padding: b(r, "pad_bytes"))
        check("petición V3 cifrada", enc == b(r, "packet"), enc.hex)

        let h = g("v3_handshake")
        check("handshake V3", V3Packet.handshake(packetID: UInt16(n(h, "packet_id")), token: b(h, "token")) == b(h, "packet"))

        let resp = g("v3_response")
        var buf = b(resp, "packet")
        if let p = V3Packet.next(from: &buf), case .data(let inner) = try V3Packet.decode(p, localKey: b(resp, "local_key")) {
            check("respuesta V3 descifrada", inner == b(resp, "data"))
        } else {
            check("respuesta V3 descifrada", false, "no se pudo extraer")
        }

        let auth = g("v3_auth")
        var hb = b(auth, "response_packet")
        if let p = V3Packet.next(from: &hb), case .handshake(let hs) = try V3Packet.decode(p, localKey: nil) {
            let lk = try V3Packet.localKey(key: b(auth, "key"), handshakeData: hs)
            check("clave de sesión V3", lk == b(auth, "local_key"), lk.hex)
        } else {
            check("clave de sesión V3", false, "no se pudo extraer")
        }
    }

    let ud = g("udpid")
    check("udpid (little endian)", MideaCrypto.udpid(deviceID: u(ud, "device_id"), bigEndian: false) == s(ud, "little"))
    check("udpid (big endian)", MideaCrypto.udpid(deviceID: u(ud, "device_id"), bigEndian: true) == s(ud, "big"))

    run("descubrimiento") {
        let d = g("discovery")
        let dev = try Discovery.parse(b(d, "response"), from: s(d, "ip"))
        let checks: [Bool] = [
            dev.ip == s(d, "ip"),
            dev.port == Int(n(d, "port")),
            dev.id == u(d, "device_id"),
            dev.name == s(d, "name"),
            dev.sn == s(d, "sn"),
            dev.type == Int(n(d, "device_type")),
            dev.version == Int(n(d, "version")),
        ]
        check("descubrimiento", checks.allSatisfy { $0 }, "\(dev)")
    }

    run("estado") {
        let d = g("state")
        let payload = try ACFrame.payload(of: b(d, "frame"))
        guard let st = ACStatus(payload: payload) else {
            check("estado", false, "no se pudo interpretar")
            return
        }
        func close(_ a: Double?, _ e: Double) -> Bool {
            guard let a = a else { return false }
            return abs(a - e) < 0.001
        }
        let checks: [Bool] = [
            st.power == t(d, "power"),
            close(st.target, n(d, "target")),
            st.mode == UInt8(n(d, "mode")),
            st.fan == UInt8(n(d, "fan")),
            st.swing == UInt8(n(d, "swing")),
            st.eco == t(d, "eco"),
            st.turbo == t(d, "turbo"),
            close(st.indoor, n(d, "indoor")),
            close(st.outdoor, n(d, "outdoor")),
            st.humidity == UInt8(n(d, "humidity")),
        ]
        check("estado", checks.allSatisfy { $0 }, "\(st)")

        let x = g("state_extras")
        guard let sx = ACStatus(payload: try ACFrame.payload(of: b(x, "frame"))) else {
            check("estado (pantalla, filtro, sleep)", false, "no se pudo interpretar")
            return
        }
        let extras: [Bool] = [
            sx.swing == UInt8(n(x, "swing")),
            sx.sleep == t(x, "sleep"),
            sx.filterAlert == t(x, "filter_alert"),
            sx.displayOn == t(x, "display_on"),
            sx.errorCode == UInt8(n(x, "error_code")),
        ]
        check("estado (pantalla, filtro, sleep)", extras.allSatisfy { $0 }, "\(sx)")
    }

    let c = g("cloud")
    check("firma de la nube", SmartHomeCloud.sign(json: s(c, "json"), random: s(c, "random")) == s(c, "sign"))
    check("hash de contraseña", SmartHomeCloud.encryptPassword(loginID: s(c, "login_id"), password: s(c, "password")) == s(c, "password_hash"))
    check("hash IAM", SmartHomeCloud.encryptIAMPassword(loginID: s(c, "login_id"), password: s(c, "password")) == s(c, "iam_hash"))

    run("AES-ECB ida y vuelta") {
        let sample = Bytes("hola aire acondicionado".utf8)
        check("AES-ECB ida y vuelta", try MideaCrypto.decryptECB(MideaCrypto.encryptECB(sample)) == sample)
    }

    print(failures == 0 ? "\nTodo coincide con msmart-ng." : "\n\(failures) comprobaciones fallaron.")
    return failures == 0 ? 0 : 1
}

// MARK: - Comandos

guard args.count >= 2 else { usage() }

switch args[1] {
case "selftest":
    exit(Int32(selftest(args.count > 2 ? args[2] : "Tests/vectors.json")))

case "discover":
    do {
        let devices = try await Setup.discover()
        if devices.isEmpty { print("No se encontró ningún aire.") }
        for d in devices {
            print("\(d.name)  IP \(d.ip):\(d.port)  ID \(d.id)  tipo 0x\(String(d.type, radix: 16))  versión \(d.version)")
        }
    } catch {
        print("Error: \(error)")
        exit(1)
    }

case "state":
    guard args.count >= 3 else { usage() }
    let ac = MideaAC(config: loadConfig(args[2]))
    ac.debug = args.contains("-v")
    do {
        print(describe(try await ac.refresh()))
    } catch {
        print("Error: \(error)")
        exit(1)
    }

case "set":
    guard args.count >= 4 else { usage() }
    let ac = MideaAC(config: loadConfig(args[2]))
    ac.debug = args.contains("-v")
    do {
        var settings = try await ac.refresh().settings
        settings.beep = true
        for arg in args.dropFirst(3) where arg.contains("=") {
            let parts = arg.split(separator: "=", maxSplits: 1).map(String.init)
            switch parts[0] {
            case "power": settings.power = ["on", "1", "si", "sí", "true"].contains(parts[1].lowercased())
            case "temp": settings.target = Double(parts[1]) ?? settings.target
            case "mode": if let m = ACMode.parse(parts[1]) { settings.mode = m.rawValue }
            case "fan": if let f = FanSpeed.parse(parts[1]) { settings.fan = f.rawValue }
            case "swing": if let w = SwingMode.parse(parts[1]) { settings.swing = w.rawValue }
            case "eco": settings.eco = isOn(parts[1])
            case "sleep": settings.sleep = isOn(parts[1])
            case "turbo": settings.turbo = isOn(parts[1])
            case "beep": settings.beep = !["off", "0", "no", "false"].contains(parts[1].lowercased())
            default: print("Ignorado: \(arg)")
            }
        }
        print(describe(try await ac.apply(settings)))
    } catch {
        print("Error: \(error)")
        exit(1)
    }

case "display":
    guard args.count >= 3 else { usage() }
    let ac = MideaAC(config: loadConfig(args[2]))
    ac.debug = args.contains("-v")
    do {
        let s = try await ac.toggleDisplay(beep: true)
        print("Pantalla \(s.displayOn ? "encendida" : "apagada")")
    } catch {
        print("Error: \(error)")
        exit(1)
    }

case "login":
    do {
        print("Buscando aires...")
        guard let device = try await Setup.discover().first else { print("No se encontró ningún aire."); exit(1) }
        print("Encontrado \(device.name) en \(device.ip)")
        print("Correo de SmartHome: ", terminator: "")
        fflush(stdout)
        let email = readLine() ?? ""
        let password = String(cString: getpass("Contraseña: "))
        let config = try await Setup.obtainKeys(for: device, account: email, password: password)
        let enc = JSONEncoder()
        enc.outputFormatting = [.prettyPrinted, .sortedKeys]
        let data = try enc.encode(config)
        FileManager.default.createFile(atPath: "aire-config.json", contents: data)
        print("OK. Guardado en aire-config.json")
    } catch {
        print("Error: \(error)")
        exit(1)
    }

default:
    usage()
}
