// aire-cli: herramienta para probar el núcleo sin la interfaz (port de CLI/main.swift).
//
//   aire-cli selftest ..\mac\Tests\vectors.json    compara con msmart-ng (no toca el aire)
//   aire-cli discover                          busca aires en la red
//   aire-cli state config.json [-v]            lee el estado
//   aire-cli set config.json power=on temp=23 mode=cool fan=auto
//   aire-cli display config.json               enciende o apaga la pantalla del aire
//   aire-cli login                             crea aire-config.json con tu cuenta SmartHome

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MideaKit;

Console.OutputEncoding = Encoding.UTF8;
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

if (args.Length < 1) return Usage();
var verbose = args.Contains("-v");

try
{
    switch (args[0])
    {
        case "selftest":
            return SelfTest.Run(args.Length > 1 ? args[1] : FindVectors());

        case "discover":
        {
            var devices = await Setup.DiscoverAsync();
            if (devices.Count == 0) Console.WriteLine("No se encontró ningún aire.");
            foreach (var d in devices)
                Console.WriteLine($"{d.Name}  IP {d.Ip}:{d.Port}  ID {d.Id}  tipo 0x{d.Type:x}  versión {d.Version}");
            return 0;
        }

        case "state":
        {
            if (args.Length < 2) return Usage();
            using var ac = new MideaAC(LoadConfig(args[1])) { Debug = verbose };
            Console.WriteLine(Describe(await ac.RefreshAsync()));
            return 0;
        }

        case "set":
        {
            if (args.Length < 3) return Usage();
            using var ac = new MideaAC(LoadConfig(args[1])) { Debug = verbose };
            var s = (await ac.RefreshAsync()).Settings;
            s.Beep = true;
            foreach (var arg in args.Skip(2).Where(a => a.Contains('=')))
            {
                var kv = arg.Split('=', 2);
                var value = kv[1];
                switch (kv[0])
                {
                    case "power": s.Power = IsOn(value); break;
                    case "temp":
                        if (double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var t))
                            s.Target = t;
                        break;
                    case "mode": if (Names.ParseMode(value) is { } m) s.Mode = (byte)m; break;
                    case "fan": if (Names.ParseFan(value) is { } f) s.Fan = (byte)f; break;
                    case "swing": if (Names.ParseSwing(value) is { } w) s.Swing = (byte)w; break;
                    case "eco": s.Eco = IsOn(value); break;
                    case "sleep": s.Sleep = IsOn(value); break;
                    case "turbo": s.Turbo = IsOn(value); break;
                    case "beep": s.Beep = IsOn(value); break;
                    default: Console.WriteLine($"Ignorado: {arg}"); break;
                }
            }
            Console.WriteLine(Describe(await ac.ApplyAsync(s)));
            return 0;
        }

        case "display":
        {
            if (args.Length < 2) return Usage();
            using var ac = new MideaAC(LoadConfig(args[1])) { Debug = verbose };
            var s = await ac.ToggleDisplayAsync(true);
            Console.WriteLine($"Pantalla {(s.DisplayOn ? "encendida" : "apagada")}");
            return 0;
        }

        case "login":
        {
            Console.WriteLine("Buscando aires...");
            var device = (await Setup.DiscoverAsync()).FirstOrDefault();
            if (device is null) { Console.WriteLine("No se encontró ningún aire."); return 1; }
            Console.WriteLine($"Encontrado {device.Name} en {device.Ip}");
            Console.Write("Correo de SmartHome: ");
            var email = Console.ReadLine()?.Trim() ?? "";
            Console.Write("Contraseña: ");
            var password = ReadPassword();
            var config = await Setup.ObtainKeysAsync(device, email, password);
            File.WriteAllText("aire-config.json", JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("OK. Guardado en aire-config.json (no lo compartas: con él se controla tu aire).");
            return 0;
        }

        default:
            return Usage();
    }
}
catch (MideaException e)
{
    Console.WriteLine($"Error: {e}");
    return 1;
}

static int Usage()
{
    Console.WriteLine("""
    Uso:
      aire-cli selftest [vectors.json]
      aire-cli discover
      aire-cli state <config.json> [-v]
      aire-cli set <config.json> power=on|off temp=23 mode=cool|heat|dry|fan|auto
                   fan=auto|silent|low|medium|high|max swing=off|vertical|horizontal|both
                   eco=on|off sleep=on|off turbo=on|off beep=on|off
      aire-cli display <config.json>      enciende o apaga la pantalla del aire
      aire-cli login                      crea aire-config.json con tu cuenta SmartHome
    """);
    return 2;
}

static bool IsOn(string s) => new[] { "on", "1", "si", "sí", "true", "yes" }.Contains(s.ToLowerInvariant());

static string FindVectors()
{
    foreach (var p in new[] { "Tests/vectors.json", "../mac/Tests/vectors.json", "../../mac/Tests/vectors.json" })
        if (File.Exists(p)) return p;
    return "../mac/Tests/vectors.json";
}

static DeviceConfig LoadConfig(string path)
{
    if (!File.Exists(path)) throw MideaException.Net($"no se puede leer {path}");
    var json = File.ReadAllText(path);
    try
    {
        var c = JsonSerializer.Deserialize<DeviceConfig>(json);
        if (c is not null && c.Id != 0 && c.Ip != "" && JsonNode.Parse(json)?["version"] is not null) return c;
    }
    catch (JsonException) { }
    return ConfigImport.Parse(json) ?? throw MideaException.Proto($"formato de {path} no reconocido");
}

static string Describe(ACStatus s)
{
    var mode = Names.ModeOf(s.Mode)?.Name() ?? $"modo {s.Mode}";
    var indoor = s.Indoor is { } i ? $"{i:0.0}°" : "?";
    var outdoor = s.Outdoor is { } o ? $"{o:0.0}°" : "?";
    var extras = new List<string>();
    var swing = Names.SwingOf(s.Swing);
    if (swing != SwingMode.Off) extras.Add($"oscila {swing.Name().ToLowerInvariant()}");
    if (s.Eco) extras.Add("eco");
    if (s.Sleep) extras.Add("sleep");
    if (s.Turbo) extras.Add("turbo");
    if (!s.DisplayOn) extras.Add("pantalla apagada");
    if (s.FilterAlert) extras.Add("LIMPIAR FILTRO");
    if (s.ErrorCode != 0) extras.Add($"error {s.ErrorCode}");
    return $"{(s.Power ? "ENCENDIDO" : "apagado")} · {mode} · objetivo {s.Target}° · ventilador {Names.FanOf(s.Fan).Name()} ({s.Fan})"
        + $" · dentro {indoor} · fuera {outdoor}" + (extras.Count == 0 ? "" : " · " + string.Join(" · ", extras));
}

static string ReadPassword()
{
    if (Console.IsInputRedirected) return Console.ReadLine() ?? "";
    var sb = new StringBuilder();
    while (true)
    {
        var k = Console.ReadKey(true);
        if (k.Key == ConsoleKey.Enter) break;
        if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; }
        else if (!char.IsControl(k.KeyChar)) sb.Append(k.KeyChar);
    }
    Console.WriteLine();
    return sb.ToString();
}

/// Compara el port en C# con los vectores generados por msmart-ng.
static class SelfTest
{
    private static int _failures;

    private static void Check(string name, bool ok, string detail = "")
    {
        Console.WriteLine((ok ? "  OK     " : "  FALLO  ") + name + (ok ? "" : "  " + detail));
        if (!ok) _failures++;
    }

    public static int Run(string path)
    {
        if (!File.Exists(path)) { Console.WriteLine($"No se pueden leer los vectores en {path}"); return 1; }
        var v = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        JsonObject G(string s) => v[s]!.AsObject();
        byte[] B(JsonObject d, string k) => Hex.TryDecode(d[k]!.GetValue<string>())!;
        double N(JsonObject d, string k) => d[k]!.GetValue<double>();
        ulong U(JsonObject d, string k) => d[k]!.GetValue<ulong>();
        bool T(JsonObject d, string k) => d[k]?.GetValue<bool>() ?? false;
        string S(JsonObject d, string k) => d[k]!.GetValue<string>();
        bool Eq(byte[] a, byte[] b) => a.AsSpan().SequenceEqual(b);
        bool Close(double? a, double e) => a is { } x && Math.Abs(x - e) < 0.001;
        void Try(string name, Action body)
        {
            try { body(); } catch (Exception e) { Check(name, false, $"error: {e.Message}"); }
        }

        Console.WriteLine("Comparando con msmart-ng:");

        var crc = G("crc8");
        Check("CRC8", Crc8.Calculate(B(crc, "input")) == (byte)N(crc, "crc"));
        Check("checksum", Crc8.Checksum(B(crc, "input")) == (byte)N(crc, "checksum"));

        var gs = G("get_state");
        var getState = ACFrame.GetState((byte)N(gs, "message_id"));
        Check("trama de consulta", Eq(getState, B(gs, "frame")), Hex.Encode(getState));

        foreach (var section in new[] { "set_state", "set_state_low", "set_state_extras" })
        {
            var d = G(section);
            var st = new ACSettings
            {
                Power = T(d, "power"),
                Target = N(d, "target"),
                Mode = (byte)N(d, "mode"),
                Fan = (byte)N(d, "fan"),
                Swing = (byte)N(d, "swing"),
                Humidity = (byte)N(d, "humidity"),
                Beep = T(d, "beep"),
                Eco = T(d, "eco"),
                Sleep = T(d, "sleep"),
                Turbo = T(d, "turbo"),
            };
            var frame = ACFrame.SetState(st, (byte)N(d, "message_id"));
            Check($"trama de control ({section})", Eq(frame, B(d, "frame")), Hex.Encode(frame));
        }

        foreach (var section in new[] { "toggle_display", "toggle_display_silent" })
        {
            var d = G(section);
            var frame = ACFrame.ToggleDisplay(T(d, "beep"), (byte)N(d, "message_id"));
            Check($"trama de pantalla ({section})", Eq(frame, B(d, "frame")), Hex.Encode(frame));
        }

        Try("paquete V2", () =>
        {
            var d = G("v2_packet");
            var packet = V2Packet.Encode(U(d, "device_id"), B(d, "frame"), B(d, "timestamp"));
            Check("paquete V2 (cifrado + firma)", Eq(packet, B(d, "packet")), Hex.Encode(packet));
            Check("paquete V2 (descifrado)", Eq(V2Packet.Decode(B(d, "packet")), B(d, "decoded")));
        });

        Try("V3", () =>
        {
            var r = G("v3_request");
            var enc = V3Packet.Encrypt((ushort)N(r, "packet_id"), B(r, "data"), B(r, "local_key"), B(r, "pad_bytes"));
            Check("petición V3 cifrada", Eq(enc, B(r, "packet")), Hex.Encode(enc));

            var h = G("v3_handshake");
            Check("handshake V3", Eq(V3Packet.Handshake((ushort)N(h, "packet_id"), B(h, "token")), B(h, "packet")));

            var resp = G("v3_response");
            var buf = new List<byte>(B(resp, "packet"));
            var p = V3Packet.Next(buf);
            Check("respuesta V3 descifrada",
                p is not null && Eq(V3Packet.Decode(p, B(resp, "local_key")).Data, B(resp, "data")) && buf.Count == 0);

            var auth = G("v3_auth");
            var hb = new List<byte>(B(auth, "response_packet"));
            var hp = V3Packet.Next(hb);
            var decoded = V3Packet.Decode(hp!, null);
            var lk = V3Packet.LocalKey(B(auth, "key"), decoded.Data);
            Check("clave de sesión V3", decoded.IsHandshake && Eq(lk, B(auth, "local_key")), Hex.Encode(lk));
        });

        var ud = G("udpid");
        Check("udpid (little endian)", MideaCrypto.UdpId(U(ud, "device_id"), false) == S(ud, "little"));
        Check("udpid (big endian)", MideaCrypto.UdpId(U(ud, "device_id"), true) == S(ud, "big"));

        Try("descubrimiento", () =>
        {
            var d = G("discovery");
            var dev = Discovery.Parse(B(d, "response"), S(d, "ip"));
            Check("descubrimiento",
                dev.Ip == S(d, "ip") && dev.Port == (int)N(d, "port") && dev.Id == U(d, "device_id")
                && dev.Name == S(d, "name") && dev.Sn == S(d, "sn") && dev.Type == (int)N(d, "device_type")
                && dev.Version == (int)N(d, "version"), dev.ToString());
        });

        Try("estado", () =>
        {
            var d = G("state");
            var st = ACStatus.Parse(ACFrame.Payload(B(d, "frame")));
            Check("estado", st is { } s
                && s.Power == T(d, "power") && Close(s.Target, N(d, "target"))
                && s.Mode == (byte)N(d, "mode") && s.Fan == (byte)N(d, "fan") && s.Swing == (byte)N(d, "swing")
                && s.Eco == T(d, "eco") && s.Turbo == T(d, "turbo")
                && Close(s.Indoor, N(d, "indoor")) && Close(s.Outdoor, N(d, "outdoor"))
                && s.Humidity == (byte)N(d, "humidity"), st?.ToString() ?? "null");

            var x = G("state_extras");
            var sx = ACStatus.Parse(ACFrame.Payload(B(x, "frame")));
            Check("estado (pantalla, filtro, sleep)", sx is { } e
                && e.Swing == (byte)N(x, "swing") && e.Sleep == T(x, "sleep") && e.FilterAlert == T(x, "filter_alert")
                && e.DisplayOn == T(x, "display_on") && e.ErrorCode == (byte)N(x, "error_code"), sx?.ToString() ?? "null");
        });

        var c = G("cloud");
        Check("firma de la nube", SmartHomeCloud.Sign(S(c, "json"), S(c, "random")) == S(c, "sign"));
        Check("hash de contraseña", SmartHomeCloud.EncryptPassword(S(c, "login_id"), S(c, "password")) == S(c, "password_hash"));
        Check("hash IAM", SmartHomeCloud.EncryptIamPassword(S(c, "login_id"), S(c, "password")) == S(c, "iam_hash"));

        Try("AES-ECB ida y vuelta", () =>
        {
            var sample = Encoding.UTF8.GetBytes("hola aire acondicionado");
            Check("AES-ECB ida y vuelta", Eq(MideaCrypto.DecryptEcb(MideaCrypto.EncryptEcb(sample)), sample));
        });

        Console.WriteLine(_failures == 0 ? "\nTodo coincide con msmart-ng." : $"\n{_failures} comprobaciones fallaron.");
        return _failures == 0 ? 0 : 1;
    }
}
