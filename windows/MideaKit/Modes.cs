// Modos, velocidades del ventilador y oscilación con sus nombres (port de MideaKit/Modes.swift).

namespace MideaKit;

public enum ACMode : byte { Auto = 1, Cool = 2, Dry = 3, Heat = 4, Fan = 5 }

public enum FanSpeed : byte { Auto = 102, Silent = 20, Low = 40, Medium = 60, High = 80, Max = 100 }

public enum SwingMode : byte { Off = 0x0, Vertical = 0xC, Horizontal = 0x3, Both = 0xF }

public static class Names
{
    public static readonly ACMode[] Modes = [ACMode.Auto, ACMode.Cool, ACMode.Dry, ACMode.Heat, ACMode.Fan];
    public static readonly FanSpeed[] Fans =
        [FanSpeed.Auto, FanSpeed.Silent, FanSpeed.Low, FanSpeed.Medium, FanSpeed.High, FanSpeed.Max];

    public static string Name(this ACMode m) => m switch
    {
        ACMode.Auto => "Auto",
        ACMode.Cool => "Frío",
        ACMode.Dry => "Seco",
        ACMode.Heat => "Calor",
        ACMode.Fan => "Ventilar",
        _ => m.ToString(),
    };

    public static string Name(this FanSpeed f) => f switch
    {
        FanSpeed.Auto => "Auto",
        FanSpeed.Silent => "Silencio",
        FanSpeed.Low => "Baja",
        FanSpeed.Medium => "Media",
        FanSpeed.High => "Alta",
        FanSpeed.Max => "Máxima",
        _ => f.ToString(),
    };

    public static string Name(this SwingMode s) => s switch
    {
        SwingMode.Off => "Fija",
        SwingMode.Vertical => "Vertical",
        SwingMode.Horizontal => "Horizontal",
        SwingMode.Both => "Ambas",
        _ => s.ToString(),
    };

    public static ACMode? ModeOf(byte raw) => Enum.IsDefined(typeof(ACMode), raw) ? (ACMode)raw : null;

    /// El aire puede devolver valores intermedios: se elige el más cercano.
    public static FanSpeed FanOf(byte raw)
    {
        if (raw > 100) return FanSpeed.Auto;
        FanSpeed[] options = [FanSpeed.Silent, FanSpeed.Low, FanSpeed.Medium, FanSpeed.High, FanSpeed.Max];
        return options.MinBy(o => Math.Abs((int)o - raw));
    }

    public static SwingMode SwingOf(byte raw)
    {
        var v = (byte)(raw & 0xF);
        return Enum.IsDefined(typeof(SwingMode), v) ? (SwingMode)v : SwingMode.Off;
    }

    public static ACMode? ParseMode(string s) => s.ToLowerInvariant() switch
    {
        "auto" => ACMode.Auto,
        "cool" or "frio" or "frío" => ACMode.Cool,
        "dry" or "seco" => ACMode.Dry,
        "heat" or "calor" => ACMode.Heat,
        "fan" or "fan_only" or "ventilar" => ACMode.Fan,
        _ => null,
    };

    public static FanSpeed? ParseFan(string s) => s.ToLowerInvariant() switch
    {
        "auto" => FanSpeed.Auto,
        "silent" or "silencio" => FanSpeed.Silent,
        "low" or "baja" => FanSpeed.Low,
        "medium" or "media" => FanSpeed.Medium,
        "high" or "alta" => FanSpeed.High,
        "max" or "maxima" or "máxima" => FanSpeed.Max,
        _ => null,
    };

    public static SwingMode? ParseSwing(string s) => s.ToLowerInvariant() switch
    {
        "off" or "fija" or "no" => SwingMode.Off,
        "vertical" or "v" => SwingMode.Vertical,
        "horizontal" or "h" => SwingMode.Horizontal,
        "both" or "ambas" or "on" => SwingMode.Both,
        _ => null,
    };
}
