// Tramas y paquetes del protocolo local de Midea (port de MideaKit/Protocol.swift, que a su vez
// es un port de msmart-ng). Cada compilación lo compara byte a byte con ../mac/Tests/vectors.json.

using System.Text;

namespace MideaKit;

// MARK: - CRC8 y checksum

public static class Crc8
{
    private static readonly byte[] Table =
    [
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
    ];

    public static byte Calculate(ReadOnlySpan<byte> data)
    {
        byte crc = 0;
        foreach (var b in data) crc = Table[crc ^ b];
        return crc;
    }

    public static byte Checksum(ReadOnlySpan<byte> data)
    {
        var sum = 0;
        foreach (var b in data) sum += b;
        return (byte)(-sum & 0xFF);
    }
}

// MARK: - Tramas del aire (0xAA ...)

public static class ACFrame
{
    public const byte DeviceType = 0xAC;
    public const byte Control = 0x02;
    public const byte Query = 0x03;

    /// Construye la trama: cabecera de 10 bytes + datos + id de mensaje + CRC + checksum.
    public static byte[] Build(byte frameType, byte[] body, byte messageId)
    {
        byte[] payload = [.. body, messageId];
        byte[] data = [.. payload, Crc8.Calculate(payload)];
        var frame = new byte[10 + data.Length + 1];
        frame[0] = 0xAA;
        frame[1] = (byte)(data.Length + 10);
        frame[2] = DeviceType;
        frame[9] = frameType;
        data.CopyTo(frame, 10);
        frame[^1] = Crc8.Checksum(frame.AsSpan(1, frame.Length - 2));
        return frame;
    }

    public static byte[] GetState(byte messageId) => Build(Query,
    [
        0x41, 0x81, 0x00, 0xFF, 0x03, 0xFF, 0x00,
        0x02, // temperatura interior
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x03,
    ], messageId);

    public static byte[] SetState(ACSettings s, byte messageId)
    {
        var integral = (int)Math.Truncate(s.Target);
        var fractional = s.Target - integral;
        byte temperature = 0, temperatureAlt = 0;
        if (integral is >= 17 and <= 30) temperature = (byte)((integral - 16) & 0xF);
        else temperatureAlt = (byte)((integral - 12) & 0x1F);
        if (fractional > 0) temperature |= 0x10;

        var mode = (byte)((s.Mode & 0x7) << 5);
        var swing = (byte)(0x30 | (s.Swing & 0x3F));
        byte power = 0x02; // origen: app
        if (s.Beep) power |= 0x40;
        if (s.Power) power |= 0x01;
        byte turboAlt = 0;
        if (s.Turbo) turboAlt |= 0x20;
        if (s.FollowMe) turboAlt |= 0x80;
        byte eco = 0;
        if (s.Eco) eco |= 0x80;
        if (s.Purifier) eco |= 0x20;
        if (s.AuxHeat) eco |= 0x08;
        byte flags = 0;
        if (s.Sleep) flags |= 0x01;
        if (s.Turbo) flags |= 0x02;
        if (s.Fahrenheit) flags |= 0x04;

        byte[] body =
        [
            0x40, power, (byte)(temperature | mode), s.Fan, 0x7F, 0x7F, 0x00, swing, turboAlt, eco, flags,
            0, 0, 0, 0, 0, 0, 0,
            temperatureAlt, (byte)(s.Humidity & 0x7F), 0x00,
            (byte)(s.FreezeProtection ? 0x80 : 0), (byte)(s.IndependentAuxHeat ? 0x08 : 0), 0x00,
        ];
        return Build(Control, body, messageId);
    }

    /// Enciende o apaga la pantalla del aire (es un conmutador: invierte su estado).
    public static byte[] ToggleDisplay(bool beep, byte messageId)
    {
        var body = new byte[21];
        body[0] = 0x41;
        body[1] = (byte)(0x02 | (beep ? 0x40 : 0));
        body[3] = 0xFF;
        body[4] = 0x02;
        body[6] = 0x02;
        return Build(Query, body, messageId);
    }

    /// Valida una trama recibida y devuelve su carga útil (sin CRC ni checksum).
    public static byte[] Payload(byte[] frame)
    {
        if (frame.Length < 13 || frame[0] != 0xAA) throw MideaException.Proto("trama demasiado corta");
        if (Crc8.Checksum(frame.AsSpan(1, frame.Length - 2)) != frame[^1])
            throw MideaException.Proto("checksum de trama incorrecto");
        if (frame[2] != DeviceType) throw MideaException.Proto("tipo de aparato inesperado");
        var body = frame[10..^1]; // carga + CRC
        var content = body.AsSpan(0, body.Length - 1);
        if (body[^1] != Crc8.Calculate(content) && body[^1] != Crc8.Checksum(content))
            throw MideaException.Proto("CRC de carga incorrecto");
        return content.ToArray();
    }
}

// MARK: - Estado

/// Lo que se envía al aire. Se rellena a partir del último estado para no tocar lo que no cambia.
public record struct ACSettings()
{
    public bool Power { get; set; } = false;
    public double Target { get; set; } = 25.0;
    public byte Mode { get; set; } = 2;
    public byte Fan { get; set; } = 102;
    public byte Swing { get; set; } = 0;
    public bool Eco { get; set; }
    public bool Turbo { get; set; }
    public bool Sleep { get; set; }
    public bool Fahrenheit { get; set; }
    public bool FollowMe { get; set; }
    public bool Purifier { get; set; }
    public byte Humidity { get; set; } = 40;
    public bool FreezeProtection { get; set; }
    public bool AuxHeat { get; set; }
    public bool IndependentAuxHeat { get; set; }
    public bool Beep { get; set; }
}

public record struct ACStatus
{
    public bool Power { get; set; }
    public double Target { get; set; }
    public byte Mode { get; set; }
    public byte Fan { get; set; }
    public byte Swing { get; set; }
    public bool Eco { get; set; }
    public bool Turbo { get; set; }
    public bool Sleep { get; set; }
    public bool Fahrenheit { get; set; }
    public bool FollowMe { get; set; }
    public bool Purifier { get; set; }
    public bool AuxHeat { get; set; }
    public bool IndependentAuxHeat { get; set; }
    public double? Indoor { get; set; }
    public double? Outdoor { get; set; }
    public byte? Humidity { get; set; }
    public bool? FreezeProtection { get; set; }
    public bool DisplayOn { get; set; }
    public bool FilterAlert { get; set; }
    public byte ErrorCode { get; set; }

    /// Interpreta la carga de una respuesta de estado (id 0xC0).
    public static ACStatus? Parse(byte[] p)
    {
        if (p.Length < 17 || p[0] != 0xC0) return null;
        var s = new ACStatus();
        s.Power = (p[1] & 0x01) != 0;
        var half = (p[2] & 0x10) != 0 ? 0.5 : 0;
        s.Target = (p[2] & 0xF) + 16 + half;
        s.Mode = (byte)((p[2] >> 5) & 0x7);
        s.Fan = (byte)(p[3] & 0x7F);
        s.Swing = (byte)(p[7] & 0xF);
        s.Turbo = (p[8] & 0x20) != 0;
        s.IndependentAuxHeat = (p[8] & 0x40) != 0;
        s.FollowMe = (p[8] & 0x80) != 0;
        s.Eco = (p[9] & 0x10) != 0;
        s.Purifier = (p[9] & 0x20) != 0;
        s.AuxHeat = (p[9] & 0x08) != 0;
        s.Sleep = (p[10] & 0x01) != 0;
        s.Turbo = s.Turbo || (p[10] & 0x02) != 0;
        s.Fahrenheit = (p[10] & 0x04) != 0;
        s.Indoor = Temperature(p[11], (p[15] & 0xF) / 10.0, s.Fahrenheit);
        s.Outdoor = Temperature(p[12], (p[15] >> 4) / 10.0, s.Fahrenheit);
        var alt = p[13] & 0x1F;
        if (alt != 0) s.Target = alt + 12 + half;
        s.FilterAlert = (p[13] & 0x20) != 0;
        s.DisplayOn = p[14] != 0x70;
        s.ErrorCode = p[16];
        if (p.Length >= 20) s.Humidity = (byte)(p[19] & 0x7F);
        if (p.Length >= 22) s.FreezeProtection = (p[21] & 0x80) != 0;
        return s;
    }

    private static double? Temperature(byte data, double decimals, bool fahrenheit)
    {
        if (data == 0xFF) return null;
        var t = (data - 50) / 2.0;
        var whole = Math.Truncate(t);
        if (!fahrenheit && decimals != 0) return whole + (t >= 0 ? decimals : -decimals);
        if (decimals >= 0.5) return whole + (t >= 0 ? 0.5 : -0.5);
        return t;
    }

    /// Ajustes para mandar al aire conservando todo lo que no se cambia.
    public readonly ACSettings Settings => new()
    {
        Power = Power,
        Target = Target,
        Mode = Mode == 0 ? (byte)2 : Mode,
        Fan = Fan,
        Swing = Swing,
        Eco = Eco,
        Turbo = Turbo,
        Sleep = Sleep,
        Fahrenheit = Fahrenheit,
        FollowMe = FollowMe,
        Purifier = Purifier,
        Humidity = Humidity ?? 40,
        FreezeProtection = FreezeProtection ?? false,
        AuxHeat = AuxHeat,
        IndependentAuxHeat = IndependentAuxHeat,
    };
}

// MARK: - Paquete V2 (0x5A5A): envuelve las tramas cifradas

public static class V2Packet
{
    public static byte[] Timestamp(DateTime? date = null)
    {
        var d = (date ?? DateTime.UtcNow).ToUniversalTime();
        return
        [
            (byte)(d.Millisecond / 10), (byte)d.Second, (byte)d.Minute, (byte)d.Hour,
            (byte)d.Day, (byte)d.Month, (byte)(d.Year % 100), (byte)(d.Year / 100),
        ];
    }

    public static byte[] Encode(ulong deviceId, byte[] frame, byte[]? timestamp = null)
    {
        var encrypted = MideaCrypto.EncryptEcb(frame);
        var length = 40 + encrypted.Length + 16;
        var header = new List<byte> { 0x5A, 0x5A, 0x01, 0x11, (byte)(length & 0xFF), (byte)(length >> 8), 0x20, 0x00, 0, 0, 0, 0 };
        header.AddRange(timestamp ?? Timestamp());
        for (var i = 0; i < 8; i++) header.Add((byte)(deviceId >> (8 * i)));
        header.AddRange(new byte[12]);
        byte[] packet = [.. header, .. encrypted];
        return [.. packet, .. MideaCrypto.Sign(packet)];
    }

    public static byte[] Decode(byte[] data)
    {
        if (data.Length < 56 || data[0] != 0x5A || data[1] != 0x5A) throw MideaException.Proto("paquete V2 no reconocido");
        var length = data[4] | data[5] << 8;
        if (length < 56 || data.Length < length) throw MideaException.Proto("paquete V2 truncado");
        var packet = data[..length];
        if (!MideaCrypto.Sign(packet.AsSpan(0, length - 16)).AsSpan().SequenceEqual(packet.AsSpan(length - 16)))
            throw MideaException.Proto("firma MD5 incorrecta");
        return MideaCrypto.DecryptEcb(packet[40..(length - 16)]);
    }
}

// MARK: - Capa V3 (0x8370): handshake y cifrado de sesión

public static class V3Packet
{
    private const byte HandshakeRequest = 0x0, HandshakeResponse = 0x1, EncryptedResponse = 0x3,
        EncryptedRequest = 0x6, Error = 0xF;

    private static byte[] Header(int length, byte extra) =>
        [0x83, 0x70, (byte)((length >> 8) & 0xFF), (byte)(length & 0xFF), 0x20, extra];

    public static byte[] Handshake(ushort packetId, byte[] token) =>
        [.. Header(token.Length, HandshakeRequest), (byte)(packetId >> 8), (byte)(packetId & 0xFF), .. token];

    public static byte[] Encrypt(ushort packetId, byte[] data, byte[] localKey, byte[]? padding = null)
    {
        var remainder = (data.Length + 2) % 16;
        var pad = remainder == 0 ? 0 : 16 - remainder;
        var h = Header(data.Length + pad + 32, (byte)((pad << 4) | EncryptedRequest));
        var filler = padding is null ? MideaCrypto.Random(pad) : padding[..pad];
        byte[] payload = [(byte)(packetId >> 8), (byte)(packetId & 0xFF), .. data, .. filler];
        var hash = MideaCrypto.Sha256([.. h, .. payload]);
        return [.. h, .. MideaCrypto.EncryptCbc(localKey, payload), .. hash];
    }

    /// Extrae el siguiente paquete completo del búfer, si lo hay.
    public static byte[]? Next(List<byte> buffer)
    {
        var start = -1;
        for (var i = 0; i + 1 < buffer.Count; i++)
            if (buffer[i] == 0x83 && buffer[i + 1] == 0x70) { start = i; break; }
        if (start < 0)
        {
            if (buffer.Count > 1) buffer.RemoveRange(0, buffer.Count - 1);
            return null;
        }
        if (start > 0) buffer.RemoveRange(0, start);
        if (buffer.Count < 6) return null;
        var total = (buffer[2] << 8 | buffer[3]) + 8;
        if (buffer.Count < total) return null;
        var packet = buffer.GetRange(0, total).ToArray();
        buffer.RemoveRange(0, total);
        return packet;
    }

    public readonly record struct Decoded(bool IsHandshake, byte[] Data);

    public static Decoded Decode(byte[] packet, byte[]? localKey)
    {
        if (packet.Length < 8 || packet[0] != 0x83 || packet[1] != 0x70) throw MideaException.Proto("inicio de paquete V3 incorrecto");
        if (packet[4] != 0x20) throw MideaException.Proto("byte mágico incorrecto");
        switch (packet[5] & 0xF)
        {
            case HandshakeResponse:
                return new Decoded(true, packet[8..]);
            case EncryptedResponse:
                if (localKey is null || packet.Length < 6 + 32 + 16) throw MideaException.Proto("respuesta cifrada sin sesión");
                var h = packet[..6];
                var body = packet[6..^32];
                var hash = packet[^32..];
                var plain = MideaCrypto.DecryptCbc(localKey, body);
                if (!MideaCrypto.Sha256([.. h, .. plain]).AsSpan().SequenceEqual(hash))
                    throw MideaException.Proto("SHA256 de respuesta incorrecto");
                var pad = h[5] >> 4;
                if (plain.Length < 2 + pad) throw MideaException.Proto("respuesta vacía");
                return new Decoded(false, plain[2..^pad]);
            case Error:
                throw MideaException.Proto("el aire devolvió un paquete de error");
            default:
                throw MideaException.Proto("tipo de paquete inesperado");
        }
    }

    /// Calcula la clave de sesión a partir de la respuesta del handshake.
    public static byte[] LocalKey(byte[] key, byte[] handshakeData)
    {
        if (handshakeData.Length != 64) throw MideaException.Auth("respuesta de handshake inválida");
        var plain = MideaCrypto.DecryptCbc(key, handshakeData[..32]);
        if (!MideaCrypto.Sha256(plain).AsSpan().SequenceEqual(handshakeData.AsSpan(32, 32)))
            throw MideaException.Auth("el token o la key no son de este aire");
        return MideaCrypto.Xor(plain, key);
    }
}

// MARK: - Descubrimiento

public sealed record DiscoveredDevice(string Ip, int Port, ulong Id, string Name, string Sn, int Type, int Version);

public static class Discovery
{
    public static readonly byte[] Message =
    [
        0x5a, 0x5a, 0x01, 0x11, 0x48, 0x00, 0x92, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x7f, 0x75, 0xbd, 0x6b, 0x3e, 0x4f, 0x8b, 0x76,
        0x2e, 0x84, 0x9c, 0x6e, 0x57, 0x8d, 0x65, 0x90, 0x03, 0x6e, 0x9d, 0x43, 0x42, 0xa5, 0x0f, 0x1f,
        0x56, 0x9e, 0xb8, 0xec, 0x91, 0x8e, 0x92, 0xe5,
    ];

    /// Interpreta la respuesta de un aire al mensaje de descubrimiento.
    public static DiscoveredDevice Parse(byte[] response, string ip)
    {
        if (response.Length < 2) throw MideaException.Proto("respuesta vacía");
        int version;
        var data = response;
        if (response[0] == 0x5A && response[1] == 0x5A) version = 2;
        else if (response[0] == 0x83 && response[1] == 0x70)
        {
            version = 3;
            if (response.Length <= 24) throw MideaException.Proto("respuesta corta");
            data = response[8..^16];
        }
        else throw MideaException.Proto("aparato no compatible (versión 1)");
        if (data.Length <= 56) throw MideaException.Proto("respuesta corta");

        ulong id = 0;
        for (var i = 0; i < 6; i++) id |= (ulong)data[20 + i] << (8 * i);
        var plain = MideaCrypto.DecryptEcb(data[40..^16]);
        if (plain.Length <= 41) throw MideaException.Proto("datos de descubrimiento cortos");
        var port = plain[4] | plain[5] << 8;
        var sn = Encoding.UTF8.GetString(plain, 8, 32);
        var nameLength = plain[40];
        if (plain.Length < 41 + nameLength) throw MideaException.Proto("nombre truncado");
        var name = Encoding.UTF8.GetString(plain, 41, nameLength);
        var parts = name.Split('_');
        var type = 0;
        if (parts.Length > 1) int.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, null, out type);
        return new DiscoveredDevice(ip, port, id, name, sn, type, version);
    }
}
