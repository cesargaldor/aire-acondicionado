// Modos y velocidades del ventilador con sus nombres.

public enum ACMode: UInt8, CaseIterable {
    case auto = 1, cool = 2, dry = 3, heat = 4, fan = 5

    public var name: String {
        switch self {
        case .auto: return "Auto"
        case .cool: return "Frío"
        case .dry: return "Seco"
        case .heat: return "Calor"
        case .fan: return "Ventilar"
        }
    }

    public var symbol: String {
        switch self {
        case .auto: return "a.circle"
        case .cool: return "snowflake"
        case .dry: return "drop"
        case .heat: return "flame"
        case .fan: return "wind"
        }
    }

    public static func parse(_ s: String) -> ACMode? {
        switch s.lowercased() {
        case "auto": return .auto
        case "cool", "frio", "frío": return .cool
        case "dry", "seco": return .dry
        case "heat", "calor": return .heat
        case "fan", "fan_only", "ventilar": return .fan
        default: return nil
        }
    }
}

public enum FanSpeed: UInt8, CaseIterable {
    case auto = 102, silent = 20, low = 40, medium = 60, high = 80, max = 100

    public var name: String {
        switch self {
        case .auto: return "Auto"
        case .silent: return "Silencio"
        case .low: return "Baja"
        case .medium: return "Media"
        case .high: return "Alta"
        case .max: return "Máxima"
        }
    }

    /// El aire puede devolver valores intermedios: se elige el más cercano.
    public init(raw: UInt8) {
        if raw > 100 { self = .auto; return }
        let options: [FanSpeed] = [.silent, .low, .medium, .high, .max]
        self = options.min { abs(Int($0.rawValue) - Int(raw)) < abs(Int($1.rawValue) - Int(raw)) } ?? .auto
    }

    public static func parse(_ s: String) -> FanSpeed? {
        switch s.lowercased() {
        case "auto": return .auto
        case "silent", "silencio": return .silent
        case "low", "baja": return .low
        case "medium", "media": return .medium
        case "high", "alta": return .high
        case "max", "maxima", "máxima": return .max
        default: return nil
        }
    }
}

/// Oscilación de las lamas (bits 0x0C vertical, 0x03 horizontal).
public enum SwingMode: UInt8, CaseIterable {
    case off = 0x0, vertical = 0xC, horizontal = 0x3, both = 0xF

    public var name: String {
        switch self {
        case .off: return "Fija"
        case .vertical: return "Vertical"
        case .horizontal: return "Horizontal"
        case .both: return "Ambas"
        }
    }

    public init(raw: UInt8) {
        self = SwingMode(rawValue: raw & 0xF) ?? .off
    }

    public static func parse(_ s: String) -> SwingMode? {
        switch s.lowercased() {
        case "off", "fija", "no": return .off
        case "vertical", "v": return .vertical
        case "horizontal", "h": return .horizontal
        case "both", "ambas", "on": return .both
        default: return nil
        }
    }
}
