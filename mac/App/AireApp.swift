// Aire: app de barra de menús que controla el aire Midea directamente, sin servidor.

import AppKit
import ServiceManagement
import SwiftUI
import UniformTypeIdentifiers

// MARK: - Modelo

@MainActor
final class ACModel: ObservableObject {
    enum Phase {
        case searching
        case notFound
        case needsLogin(DiscoveredDevice)
        case ready
    }

    @Published var phase: Phase = .searching
    @Published var status: ACStatus?
    @Published var online = false
    @Published var busy = false
    @Published var message: String?
    @Published var syncing = 0  // órdenes enviadas sin respuesta todavía
    @Published var beep: Bool = UserDefaults.standard.object(forKey: "beep") as? Bool ?? true {
        didSet { UserDefaults.standard.set(beep, forKey: "beep") }
    }

    private var device: MideaAC?
    private var pendingTarget: Double?
    private var debounce: Task<Void, Never>?
    private var poller: Task<Void, Never>?

    init() {
        if let saved = ConfigStore.load(), !saved.needsKeys {
            use(saved)
        } else {
            Task { await discover() }
        }
        poller = Task { [weak self] in
            while !Task.isCancelled {
                try? await Task.sleep(nanoseconds: 30_000_000_000)
                await self?.refresh()
            }
        }
    }

    var isReady: Bool {
        if case .ready = phase { return true }
        return false
    }

    private func use(_ config: DeviceConfig) {
        device?.close()
        device = MideaAC(config: config)
        phase = .ready
        Task { await refresh() }
    }

    // MARK: Configuración

    func discover() async {
        phase = .searching
        message = nil
        do {
            let found = try await Setup.discover()
            guard let d = found.first(where: { $0.type == 0xAC }) ?? found.first else {
                phase = .notFound
                return
            }
            if d.version == 3 {
                phase = .needsLogin(d)
            } else {
                let config = DeviceConfig(d)
                ConfigStore.save(config)
                use(config)
            }
        } catch {
            message = "\(error)"
            phase = .notFound
        }
    }

    func login(_ d: DiscoveredDevice, email: String, password: String) async {
        busy = true
        message = nil
        defer { busy = false }
        do {
            let config = try await Setup.obtainKeys(for: d, account: email.trimmingCharacters(in: .whitespaces),
                                                    password: password)
            ConfigStore.save(config)
            use(config)
        } catch MideaError.cloud(let msg, let code) {
            message = code == 3102 ? "Correo o contraseña incorrectos." : "Midea: \(msg) (\(code))"
        } catch {
            message = "\(error)"
        }
    }

    /// Importa un config.json con ip, id, token y key, sin iniciar sesión en la nube.
    func importConfig() {
        NSApp.activate(ignoringOtherApps: true)
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.json]
        panel.message = "Elige un config.json con ip, id, token y key"
        guard panel.runModal() == .OK, let url = panel.url,
              let data = try? Data(contentsOf: url),
              let config = ConfigStore.importPythonConfig(data)
        else { return }
        busy = true
        message = nil
        Task {
            defer { busy = false }
            let test = MideaAC(config: config)
            defer { test.close() }
            do {
                if config.version == 3 { try await test.authenticate() }
                ConfigStore.save(config)
                use(config)
            } catch {
                message = "Ese config.json no funciona con el aire: \(error)"
            }
        }
    }

    func forget() {
        ConfigStore.delete()
        device?.close()
        device = nil
        status = nil
        online = false
        Task { await discover() }
    }

    // MARK: Control

    func refresh() async {
        guard let device, pendingTarget == nil else { return }
        do {
            status = try await device.refresh()
            online = true
        } catch {
            online = false
            await relocate()
        }
    }

    /// Si el aire cambió de IP (el router le dio otra), lo vuelve a encontrar por su ID.
    private func relocate() async {
        guard let device, let found = try? await Setup.discover(),
              let d = found.first(where: { $0.id == device.config.id }), d.ip != device.config.ip
        else { return }
        device.updateIP(d.ip, port: d.port)
        ConfigStore.save(device.config)
        if let s = try? await device.refresh() {
            status = s
            online = true
        }
    }

    func change(_ edit: (inout ACSettings) -> Void) {
        guard let device, let current = status else { return }
        var settings = current.settings
        edit(&settings)
        settings.beep = beep  // pitido del aire al recibir la orden

        // Respuesta inmediata en pantalla
        var shown = current
        shown.power = settings.power
        shown.target = settings.target
        shown.mode = settings.mode
        shown.fan = settings.fan
        shown.swing = settings.swing
        shown.eco = settings.eco
        shown.sleep = settings.sleep
        shown.turbo = settings.turbo
        status = shown

        syncing += 1
        Task {
            defer { syncing -= 1 }
            do {
                status = try await device.apply(settings)
                online = true
            } catch {
                online = false
                message = "No se pudo enviar la orden: \(error)"
                await refresh()
            }
        }
    }

    /// Las pulsaciones seguidas de +/− se agrupan en una sola orden.
    func step(_ delta: Double) {
        guard let current = status else { return }
        setTarget((pendingTarget ?? current.target) + delta)
    }

    /// Mientras se arrastra la barra solo cambia la pantalla; la orden sale al soltar o parar.
    func setTarget(_ value: Double) {
        guard status != nil else { return }
        let target = min(30, max(16, value.rounded()))
        guard target != status?.target || pendingTarget != nil else { return }
        pendingTarget = target
        status?.target = target
        debounce?.cancel()
        debounce = Task {
            try? await Task.sleep(nanoseconds: 600_000_000)
            guard !Task.isCancelled else { return }
            pendingTarget = nil
            change { $0.target = target }
        }
    }
}

// MARK: - Barra de menús

func symbol(_ name: String, _ fallback: String) -> String {
    NSImage(systemSymbolName: name, accessibilityDescription: nil) != nil ? name : fallback
}

struct MenuLabel: View {
    @ObservedObject var model: ACModel

    var body: some View {
        HStack(spacing: 4) {
            Image(systemName: symbol("air.conditioner.horizontal", "thermometer.medium"))
            if model.isReady {
                if !model.online {
                    Text("–")
                } else if let s = model.status, s.power {
                    Text("\(Int(s.target.rounded()))°")
                }
            }
        }
    }
}

// MARK: - Estilo

enum Theme {
    static let background = Color(red: 0.075, green: 0.071, blue: 0.058)
    static let cream = Color(red: 0.94, green: 0.92, blue: 0.85)
    static let muted = cream.opacity(0.55)
    static let card = Color.white.opacity(0.045)
    static let stroke = Color.white.opacity(0.07)
    static let olive = Color(red: 0.70, green: 0.64, blue: 0.44)
    static let ember = Color(red: 0.90, green: 0.56, blue: 0.30)

    static func accent(_ s: ACStatus) -> Color {
        s.mode == ACMode.heat.rawValue ? ember : olive
    }
}

extension View {
    func cardStyle(_ fill: Color = Theme.card, radius: CGFloat = 14) -> some View {
        background(RoundedRectangle(cornerRadius: radius, style: .continuous).fill(fill))
            .overlay(RoundedRectangle(cornerRadius: radius, style: .continuous).stroke(Theme.stroke, lineWidth: 1))
    }
}

// MARK: - Piezas de la interfaz

/// El aire dibujado, echando aire cuando está encendido.
struct ACUnitView: View {
    let status: ACStatus

    var body: some View {
        let accent = Theme.accent(status)
        ZStack(alignment: .top) {
            AirflowView(active: status.power, color: accent,
                        strength: status.power ? strength : 0, swing: SwingMode(raw: status.swing))
                .frame(width: 210, height: 90)
                .offset(y: 50)

            ZStack(alignment: .bottom) {
                RoundedRectangle(cornerRadius: 12, style: .continuous)
                    .fill(LinearGradient(colors: [Color(white: 0.98), Color(red: 0.84, green: 0.82, blue: 0.75)],
                                         startPoint: .top, endPoint: .bottom))
                RoundedRectangle(cornerRadius: 3)
                    .fill(Color.black.opacity(0.55))
                    .frame(height: 5)
                    .padding(.horizontal, 14)
                    .padding(.bottom, 7)
                if status.displayOn && status.power {
                    Text("\(Int(status.target.rounded()))")
                        .font(.system(size: 8, weight: .bold, design: .monospaced))
                        .foregroundColor(accent.opacity(0.9))
                        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topTrailing)
                        .padding(.trailing, 26).padding(.top, 9)
                }
            }
            .frame(width: 200, height: 54)
            .shadow(color: .black.opacity(0.5), radius: 10, y: 6)
        }
        .rotation3DEffect(.degrees(-14), axis: (x: 0, y: 1, z: 0), perspective: 0.6)
        .frame(height: 140)
        .frame(maxWidth: .infinity)
    }

    private var strength: Double {
        switch FanSpeed(raw: status.fan) {
        case .silent: return 0.25
        case .low: return 0.4
        case .medium: return 0.6
        case .high, .auto: return 0.8
        case .max: return 1
        }
    }
}

struct AirflowView: View {
    let active: Bool
    let color: Color
    let strength: Double
    let swing: SwingMode

    var body: some View {
        TimelineView(.animation(minimumInterval: 1.0 / 30, paused: !active)) { timeline in
            let t = timeline.date.timeIntervalSinceReferenceDate
            Canvas { g, size in
                guard active else { return }
                let lines = 22
                let vertical = swing == .vertical || swing == .both
                let horizontal = swing == .horizontal || swing == .both
                let reach = size.height * (0.5 + 0.5 * strength) * (vertical ? 0.8 + 0.2 * sin(t * 1.1) : 1)
                let sway = horizontal ? sin(t * 0.8) * 18 : 0
                for i in 0..<lines {
                    let f = Double(i) / Double(lines - 1)
                    let x0 = size.width * (0.1 + 0.8 * f)
                    var path = Path()
                    for s in 0...14 {
                        let k = Double(s) / 14
                        let wobble = sin(k * 5 + t * (2 + 3 * strength) + Double(i) * 1.7) * 3 * k
                        let x = x0 + (f - 0.5) * 40 * k + sway * k + wobble
                        let point = CGPoint(x: x, y: k * reach)
                        if s == 0 { path.move(to: point) } else { path.addLine(to: point) }
                    }
                    let pulse = 0.55 + 0.45 * sin(t * 2.4 + Double(i) * 0.9)
                    g.stroke(path, with: .linearGradient(
                        Gradient(colors: [color.opacity(0.55 * pulse), color.opacity(0)]),
                        startPoint: CGPoint(x: x0, y: 0), endPoint: CGPoint(x: x0, y: reach)),
                        lineWidth: 1.6)
                }
            }
        }
        .blur(radius: 0.6)
        .allowsHitTesting(false)
    }
}

struct PillToggle: View {
    let isOn: Bool
    let tint: Color

    var body: some View {
        ZStack(alignment: isOn ? .trailing : .leading) {
            Capsule().fill(isOn ? tint.opacity(0.85) : Color.white.opacity(0.08))
            Capsule().fill(Theme.cream).frame(width: 26, height: 18).padding(3)
        }
        .frame(width: 50, height: 24)
        .animation(.spring(response: 0.3, dampingFraction: 0.8), value: isOn)
    }
}

struct FeaturePill: View {
    let icon: String
    let title: String
    let isOn: Bool
    let tint: Color
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            HStack(spacing: 10) {
                Image(systemName: icon).font(.system(size: 15)).foregroundColor(isOn ? tint : Theme.muted)
                Text(title).font(.system(size: 13, weight: .medium)).foregroundColor(Theme.cream)
                    .lineLimit(1).fixedSize()
                Spacer(minLength: 4)
                PillToggle(isOn: isOn, tint: tint)
            }
            .padding(.horizontal, 11).padding(.vertical, 9)
            .cardStyle(isOn ? tint.opacity(0.12) : Theme.card)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }
}

struct ControlCard: View {
    enum Accessory { case chevron(Bool), dot(Bool) }

    let icon: String
    let title: String
    let value: String
    let accessory: Accessory
    let tint: Color
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            VStack(alignment: .leading, spacing: 2) {
                HStack(alignment: .top) {
                    Image(systemName: icon).font(.system(size: 20, weight: .light))
                        .foregroundColor(highlighted ? tint : Theme.cream)
                    Spacer()
                    switch accessory {
                    case .chevron(let open):
                        Image(systemName: "chevron.down").font(.system(size: 10, weight: .semibold))
                            .rotationEffect(.degrees(open ? 180 : 0)).foregroundColor(Theme.muted)
                    case .dot(let on):
                        Circle().fill(on ? tint : Color.white.opacity(0.2)).frame(width: 6, height: 6)
                            .shadow(color: on ? tint : .clear, radius: 4)
                    }
                }
                Spacer(minLength: 8)
                Text(title).font(.system(size: 11)).foregroundColor(Theme.muted)
                Text(value).font(.system(size: 13, weight: .semibold)).foregroundColor(Theme.cream)
                    .lineLimit(1).minimumScaleFactor(0.8)
            }
            .padding(10)
            .frame(maxWidth: .infinity, minHeight: 84, alignment: .leading)
            .cardStyle(highlighted ? tint.opacity(0.12) : Theme.card)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }

    private var highlighted: Bool {
        switch accessory {
        case .chevron(let open): return open
        case .dot(let on): return on
        }
    }
}

struct OptionChip: View {
    let title: String
    let selected: Bool
    let tint: Color
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Text(title)
                .font(.system(size: 12, weight: selected ? .semibold : .regular))
                .lineLimit(1).minimumScaleFactor(0.75)
                .foregroundColor(selected ? Theme.background : Theme.cream)
                .padding(.vertical, 7)
                .frame(maxWidth: .infinity)
                .background(Capsule().fill(selected ? tint : Color.white.opacity(0.06)))
                .contentShape(Capsule())
        }
        .buttonStyle(.plain)
    }
}

struct CircleButton: View {
    let systemName: String
    var size: CGFloat = 40
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Image(systemName: systemName)
                .font(.system(size: 15, weight: .medium))
                .foregroundColor(Theme.cream)
                .frame(width: size, height: size)
                .background(Circle().fill(Color.white.opacity(0.06)))
                .overlay(Circle().stroke(Theme.stroke, lineWidth: 1))
                .contentShape(Circle())
        }
        .buttonStyle(.plain)
    }
}

struct TemperatureSlider: View {
    let value: Double
    let tint: Color
    let onChange: (Double) -> Void
    private let range = 16.0...30.0

    var body: some View {
        GeometryReader { geo in
            let knob: CGFloat = 30
            let width = geo.size.width
            let fraction = CGFloat((value - range.lowerBound) / (range.upperBound - range.lowerBound))
            let x: CGFloat = 6 + (width - knob - 12) * fraction
            ZStack(alignment: .leading) {
                Capsule().fill(Color.black.opacity(0.35))
                    .overlay(Capsule().stroke(Theme.stroke, lineWidth: 1))
                Capsule()
                    .fill(LinearGradient(colors: [tint.opacity(0.15), tint.opacity(0.75)],
                                         startPoint: .leading, endPoint: .trailing))
                    .frame(width: x + knob + 6)
                RoundedRectangle(cornerRadius: 9, style: .continuous)
                    .fill(Theme.cream)
                    .frame(width: knob, height: geo.size.height - 12)
                    .shadow(color: .black.opacity(0.4), radius: 4, y: 2)
                    .offset(x: x)
            }
            .contentShape(Rectangle())
            .gesture(DragGesture(minimumDistance: 0).onChanged { drag in
                let f = (drag.location.x - 6 - knob / 2) / (width - knob - 12)
                onChange(range.lowerBound + Double(min(1, max(0, f))) * (range.upperBound - range.lowerBound))
            })
            .animation(.interactiveSpring(), value: value)
        }
        .frame(height: 48)
    }
}

// MARK: - Panel

struct PanelView: View {
    enum Choice { case mode, fan }

    @EnvironmentObject var m: ACModel
    @State private var email = ""
    @State private var password = ""
    @State private var showSettings = false
    @State private var picker: Choice?
    @State private var launchAtLogin = SMAppService.mainApp.status == .enabled

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            switch m.phase {
            case .searching:
                title
                HStack(spacing: 8) {
                    ProgressView().controlSize(.small)
                    Text("Buscando el aire en tu red…").foregroundColor(Theme.muted)
                }
            case .notFound:
                title
                notFound
            case .needsLogin(let d):
                title
                loginForm(d)
            case .ready:
                if let s = m.status {
                    controls(s)
                } else {
                    title
                    if m.online {
                        ProgressView().controlSize(.small)
                    } else {
                        Text("No responde. Comprueba que el Mac está en la misma WiFi que el aire.")
                            .font(.callout).foregroundColor(Theme.muted)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                }
            }
            if let msg = m.message {
                Text(msg).font(.caption).foregroundColor(Theme.ember)
                    .fixedSize(horizontal: false, vertical: true)
            }
            footer
        }
        .padding(18)
        .frame(width: 370)
        .background(Theme.background)
        .foregroundColor(Theme.cream)
        .environment(\.colorScheme, .dark)
        .animation(.easeInOut(duration: 0.2), value: picker)
        .animation(.easeInOut(duration: 0.2), value: showSettings)
        .onAppear { Task { await m.refresh() } }
    }

    private var title: some View {
        HStack(spacing: 8) {
            Image(systemName: symbol("air.conditioner.horizontal", "thermometer.medium"))
            Text("Aire").font(.system(size: 15, weight: .semibold))
        }
    }

    private var notFound: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("No se encontró ningún aire Midea. Comprueba que el Mac está en la misma WiFi.")
                .font(.callout).foregroundColor(Theme.muted)
                .fixedSize(horizontal: false, vertical: true)
            HStack {
                Button("Buscar de nuevo") { Task { await m.discover() } }
                Button("Importar config.json") { m.importConfig() }
            }
        }
    }

    private func loginForm(_ d: DiscoveredDevice) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Encontrado **\(d.name)** en \(d.ip)").font(.callout)
            Text("La primera vez hace falta tu cuenta de SmartHome para obtener la clave del aire. La contraseña no se guarda; después todo funciona en local.")
                .font(.caption).foregroundColor(Theme.muted)
                .fixedSize(horizontal: false, vertical: true)
            TextField("Correo", text: $email)
                .textFieldStyle(.roundedBorder)
                .textContentType(.username)
            SecureField("Contraseña", text: $password)
                .textFieldStyle(.roundedBorder)
                .onSubmit { connect(d) }
            HStack {
                Button("Importar config.json") { m.importConfig() }
                    .buttonStyle(.borderless).font(.caption)
                Spacer()
                if m.busy { ProgressView().controlSize(.small) }
                Button("Conectar") { connect(d) }
                    .keyboardShortcut(.defaultAction)
                    .disabled(m.busy || email.isEmpty || password.isEmpty)
            }
        }
        .onAppear { NSApp.activate(ignoringOtherApps: true) }
    }

    private func connect(_ d: DiscoveredDevice) {
        guard !email.isEmpty, !password.isEmpty else { return }
        let pwd = password
        password = ""  // no se queda en memoria de la vista
        Task { await m.login(d, email: email, password: pwd) }
    }

    @ViewBuilder
    private func controls(_ s: ACStatus) -> some View {
        let tint = Theme.accent(s)
        let mode = ACMode(rawValue: s.mode) ?? .cool
        let fan = FanSpeed(raw: s.fan)

        // Sala y encendido
        HStack(alignment: .center) {
            Image(systemName: "thermometer.medium").foregroundColor(Theme.muted)
            Text("Sala").foregroundColor(Theme.muted)
            if let i = s.indoor {
                Text(String(format: "%.1f°C", i)).fontWeight(.semibold)
            }
            if let o = s.outdoor {
                Text("· Fuera " + String(format: "%.0f°", o)).foregroundColor(Theme.muted)
            }
            Spacer()
            Button { m.change { $0.power = !s.power } } label: {
                Image(systemName: "power")
                    .font(.system(size: 18, weight: .medium))
                    .foregroundColor(s.power ? Theme.background : Theme.cream)
                    .frame(width: 44, height: 44)
                    .background(Circle().fill(s.power ? tint : Color.white.opacity(0.06)))
                    .overlay(Circle().stroke(s.power ? tint.opacity(0.4) : Theme.stroke, lineWidth: 4).padding(-3))
                    .shadow(color: s.power ? tint.opacity(0.5) : .clear, radius: 10)
                    .contentShape(Circle())
            }
            .buttonStyle(.plain)
            .help(s.power ? "Apagar" : "Encender")
        }
        .font(.system(size: 14))

        ACUnitView(status: s)
            .opacity(s.power ? 1 : 0.55)

        // Temperatura y turbo
        HStack(alignment: .center) {
            HStack(alignment: .firstTextBaseline, spacing: 4) {
                Text(String(format: s.target.truncatingRemainder(dividingBy: 1) == 0 ? "%.0f" : "%.1f", s.target))
                    .font(.system(size: 56, weight: .semibold))
                    .monospacedDigit()
                Text("°C").font(.system(size: 22, weight: .medium)).foregroundColor(Theme.muted)
            }
            .opacity(s.power ? 1 : 0.45)
            Spacer()
            FeaturePill(icon: "bolt", title: "Turbo", isOn: s.turbo, tint: tint) {
                m.change { $0.turbo.toggle() }
            }
            .frame(width: 160)
        }

        VStack(spacing: 6) {
            HStack(spacing: 10) {
                CircleButton(systemName: "minus") { m.step(-1) }
                TemperatureSlider(value: s.target, tint: tint) { m.setTarget($0) }
                CircleButton(systemName: "plus") { m.step(1) }
            }
            HStack {
                Text("16°")
                Spacer()
                Text("30°")
            }
            .font(.system(size: 11)).foregroundColor(Theme.muted)
            .padding(.horizontal, 56)
        }

        // Tarjetas
        HStack(spacing: 8) {
            ControlCard(icon: mode.symbol, title: "Modo", value: mode.name,
                        accessory: .chevron(picker == .mode), tint: tint) { toggle(.mode) }
            ControlCard(icon: symbol("fanblades", "fan"), title: "Ventilador", value: fan.name,
                        accessory: .chevron(picker == .fan), tint: tint) { toggle(.fan) }
        }

        if let picker {
            HStack(spacing: 6) {
                switch picker {
                case .mode:
                    ForEach(ACMode.allCases, id: \.rawValue) { option in
                        OptionChip(title: option.name, selected: s.power && mode == option, tint: tint) {
                            m.change {
                                $0.power = true
                                $0.mode = option.rawValue
                            }
                        }
                    }
                case .fan:
                    ForEach(FanSpeed.allCases, id: \.rawValue) { option in
                        OptionChip(title: option.name, selected: fan == option, tint: tint) {
                            m.change { $0.fan = option.rawValue }
                        }
                    }
                }
            }
            .padding(6)
            .cardStyle(radius: 20)
            .transition(.opacity.combined(with: .move(edge: .top)))
        }

        if s.filterAlert || s.errorCode != 0 {
            HStack(spacing: 8) {
                Image(systemName: "exclamationmark.triangle.fill").foregroundColor(Theme.ember)
                Text(s.filterAlert ? "Toca limpiar el filtro del aire." : "El aire indica el error \(s.errorCode).")
                    .font(.system(size: 12))
                Spacer()
            }
            .padding(10)
            .cardStyle(Theme.ember.opacity(0.1))
        }
    }

    private func toggle(_ p: Choice) {
        picker = picker == p ? nil : p
    }

    private var statusLine: some View {
        HStack(spacing: 6) {
            if m.syncing > 0 {
                ProgressView().controlSize(.mini)
                Text("Sincronizando…")
            } else if m.isReady {
                Image(systemName: m.online ? "checkmark.shield" : "exclamationmark.shield")
                Text(m.online ? "Conectado" : "Sin conexión")
            }
        }
        .font(.system(size: 12))
        .foregroundColor(Theme.muted)
    }

    private var footer: some View {
        VStack(alignment: .leading, spacing: 12) {
            if showSettings {
                VStack(alignment: .leading, spacing: 10) {
                    Toggle("Pitido del aire al cambiar algo", isOn: $m.beep)
                    Toggle("Abrir al iniciar sesión", isOn: $launchAtLogin)
                        .onChange(of: launchAtLogin) { value in
                            do {
                                if value { try SMAppService.mainApp.register() }
                                else { try SMAppService.mainApp.unregister() }
                            } catch {
                                launchAtLogin = SMAppService.mainApp.status == .enabled
                            }
                        }
                    HStack {
                        if m.isReady {
                            Button("Olvidar este aire") { m.forget() }
                        }
                        Spacer()
                        Button("Salir de Aire") { NSApp.terminate(nil) }
                    }
                    .buttonStyle(.borderless)
                    .foregroundColor(Theme.muted)
                }
                .font(.system(size: 12))
                .toggleStyle(.switch)
                .controlSize(.small)
                .padding(12)
                .cardStyle()
                .transition(.opacity)
            }
            HStack {
                statusLine
                Spacer()
                Button { showSettings.toggle() } label: {
                    Image(systemName: "slider.horizontal.3")
                        .font(.system(size: 14))
                        .foregroundColor(showSettings ? Theme.cream : Theme.muted)
                        .frame(width: 28, height: 24)
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .help("Ajustes")
            }
        }
    }
}

// MARK: - App

@main
struct AireApp: App {
    @StateObject private var model = ACModel()

    var body: some Scene {
        MenuBarExtra {
            PanelView().environmentObject(model)
        } label: {
            MenuLabel(model: model)
        }
        .menuBarExtraStyle(.window)
    }
}
