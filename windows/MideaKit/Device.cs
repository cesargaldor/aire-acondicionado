// Un aire Midea en la red local: conexión, autenticación V3, consulta y control (port de Device.swift).

using System.Text.Json.Serialization;

namespace MideaKit;

public sealed class DeviceConfig
{
    [JsonPropertyName("ip")] public string Ip { get; set; } = "";
    [JsonPropertyName("port")] public int Port { get; set; } = 6444;
    [JsonPropertyName("id")] public ulong Id { get; set; }
    [JsonPropertyName("version")] public int Version { get; set; } = 3;
    [JsonPropertyName("token")] public string? Token { get; set; }
    [JsonPropertyName("key")] public string? Key { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }

    public DeviceConfig() { }

    public DeviceConfig(DiscoveredDevice d)
    {
        Ip = d.Ip;
        Port = d.Port;
        Id = d.Id;
        Version = d.Version;
        Name = d.Name;
    }

    [JsonIgnore] public bool NeedsKeys => Version == 3 && (Token is null || Key is null);

    public DeviceConfig Copy() => (DeviceConfig)MemberwiseClone();
}

public sealed class MideaAC : IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly TcpConnection _conn = new();
    private readonly List<byte> _buffer = [];
    private byte[]? _localKey;
    private DateTime _authExpiry = DateTime.MinValue;
    private ushort _packetId;
    private byte _messageId;

    public DeviceConfig Config { get; private set; }
    public ACStatus? LastStatus { get; private set; }

    /// Si se activa, escribe el tráfico en la consola (para depurar).
    public bool Debug { get; set; }

    public MideaAC(DeviceConfig config) => Config = config.Copy();

    // MARK: API asíncrona

    public Task<ACStatus> RefreshAsync() => Run(() => Perform(() => ACFrame.GetState(NextMessageId())));

    public Task<ACStatus> ApplyAsync(ACSettings settings) =>
        Run(() => Perform(() => ACFrame.SetState(settings, NextMessageId())));

    /// Enciende o apaga la pantalla del aire y devuelve el estado nuevo.
    public Task<ACStatus> ToggleDisplayAsync(bool beep) => Run(() =>
    {
        // Es un conmutador: se envía una sola vez, sin reintentos, para no invertirlo dos veces.
        SendOnce(ACFrame.ToggleDisplay(beep, NextMessageId()));
        return Perform(() => ACFrame.GetState(NextMessageId()));
    });

    /// Comprueba que el token y la key funcionan con este aire.
    public Task AuthenticateAsync() => Run(() =>
    {
        Disconnect();
        EnsureSession();
        return true;
    });

    public async Task UpdateIpAsync(string ip, int port)
    {
        await _lock.WaitAsync();
        try
        {
            Config.Ip = ip;
            Config.Port = port;
            Disconnect();
        }
        finally { _lock.Release(); }
    }

    public void Dispose()
    {
        _ = Task.Run(async () =>
        {
            await _lock.WaitAsync();
            try { Disconnect(); } finally { _lock.Release(); }
        });
    }

    private async Task<T> Run<T>(Func<T> work)
    {
        await _lock.WaitAsync();
        try { return await Task.Run(work); }
        finally { _lock.Release(); }
    }

    // MARK: Sesión (siempre con el cerrojo tomado)

    private void Log(Func<string> s)
    {
        if (Debug) Console.WriteLine($"[aire] {s()}");
    }

    private byte NextMessageId() => unchecked(++_messageId);

    private ushort NextPacketId()
    {
        var id = _packetId;
        _packetId = (ushort)((_packetId + 1) & 0xFFF);
        return id;
    }

    private void Disconnect()
    {
        _conn.Close();
        _localKey = null;
        _buffer.Clear();
    }

    private void EnsureSession()
    {
        if (!_conn.IsOpen)
        {
            Disconnect();
            Log(() => $"conectando con {Config.Ip}:{Config.Port}");
            _conn.Connect(Config.Ip, Config.Port);
            _packetId = 0;
        }
        if (Config.Version == 3 && (_localKey is null || DateTime.UtcNow > _authExpiry)) Handshake();
    }

    private void Handshake()
    {
        var token = Hex.TryDecode(Config.Token);
        var key = Hex.TryDecode(Config.Key);
        if (token is null || key is null || token.Length == 0 || key.Length == 0)
            throw MideaException.Auth("faltan el token y la key");

        _buffer.Clear();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var packet = V3Packet.Handshake(NextPacketId(), token);
            Log(() => $"handshake → {Hex.Encode(packet)}");
            _conn.Send(packet);
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (ReadPacket(deadline) is { } response)
            {
                Log(() => $"handshake ← {Hex.Encode(response)}");
                try
                {
                    var decoded = V3Packet.Decode(response, null);
                    if (decoded.IsHandshake)
                    {
                        _localKey = V3Packet.LocalKey(key, decoded.Data);
                        _authExpiry = DateTime.UtcNow.AddHours(11);
                        Log(() => "autenticado");
                        Thread.Sleep(1000); // el aire necesita un respiro
                        return;
                    }
                }
                catch (MideaException e)
                {
                    Disconnect();
                    throw MideaException.Auth(e.ToString());
                }
            }
        }
        Disconnect();
        throw MideaException.Timeout();
    }

    /// Devuelve el siguiente paquete completo o null si se agota el tiempo.
    private byte[]? ReadPacket(DateTime deadline)
    {
        while (true)
        {
            if (ExtractPacket() is { } p) return p;
            var remaining = (deadline - DateTime.UtcNow).TotalSeconds;
            if (remaining <= 0) return null;
            var chunk = _conn.Receive(remaining);
            if (chunk.Length == 0) return null;
            _buffer.AddRange(chunk);
        }
    }

    private byte[]? ExtractPacket()
    {
        if (Config.Version == 3) return V3Packet.Next(_buffer);
        // V2: 5A5A + longitud en little endian
        var start = -1;
        for (var i = 0; i + 1 < _buffer.Count; i++)
            if (_buffer[i] == 0x5A && _buffer[i + 1] == 0x5A) { start = i; break; }
        if (start < 0) return null;
        if (start > 0) _buffer.RemoveRange(0, start);
        if (_buffer.Count < 6) return null;
        var length = _buffer[4] | _buffer[5] << 8;
        if (length < 6 || _buffer.Count < length) return null;
        var packet = _buffer.GetRange(0, length).ToArray();
        _buffer.RemoveRange(0, length);
        return packet;
    }

    private byte[] FrameFrom(byte[] packet)
    {
        if (Config.Version == 3)
        {
            var decoded = V3Packet.Decode(packet, _localKey);
            if (decoded.IsHandshake) throw MideaException.Proto("paquete inesperado");
            return V2Packet.Decode(decoded.Data);
        }
        return V2Packet.Decode(packet);
    }

    private void Send(byte[] cmd)
    {
        Log(() => $"trama → {Hex.Encode(cmd)}");
        var packet = V2Packet.Encode(Config.Id, cmd);
        if (Config.Version == 3)
        {
            if (_localKey is null) throw MideaException.Auth("sin sesión");
            _conn.Send(V3Packet.Encrypt(NextPacketId(), packet, _localKey));
        }
        else _conn.Send(packet);
    }

    /// Envía una trama sin esperar estado y descarta lo que conteste.
    private void SendOnce(byte[] cmd)
    {
        EnsureSession();
        Send(cmd);
        var deadline = DateTime.UtcNow.AddSeconds(1);
        while (ReadPacket(deadline) is { } response)
        {
            try { var f = FrameFrom(response); Log(() => $"trama ← {Hex.Encode(f)} (descartada)"); }
            catch (MideaException) { }
        }
    }

    private ACStatus Exchange(Func<byte[]> makeFrame)
    {
        EnsureSession();

        // Descarta respuestas antiguas que se hayan quedado en el búfer
        try
        {
            var chunk = _conn.Receive(0.05);
            if (chunk.Length > 0) _buffer.AddRange(chunk);
        }
        catch (MideaException) { }
        while (ExtractPacket() is not null) { }
        EnsureSession(); // por si la lectura anterior detectó la conexión cerrada

        for (var attempt = 0; attempt < 3; attempt++)
        {
            Send(makeFrame());
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (ReadPacket(deadline) is { } response)
            {
                try
                {
                    var f = FrameFrom(response);
                    Log(() => $"trama ← {Hex.Encode(f)}");
                    if (ACStatus.Parse(ACFrame.Payload(f)) is { } status)
                    {
                        LastStatus = status;
                        return status;
                    }
                }
                catch (MideaException e) { Log(() => $"respuesta ignorada: {e}"); }
            }
            Log(() => "sin respuesta, reintentando");
        }
        Disconnect();
        throw MideaException.Timeout();
    }

    /// Ejecuta un intercambio y, si la conexión se había caído, reconecta una vez.
    private ACStatus Perform(Func<byte[]> makeFrame)
    {
        try { return Exchange(makeFrame); }
        catch (MideaException e) when (e.Kind == MideaErrorKind.Authentication) { throw; }
        catch (MideaException e)
        {
            Log(() => $"reintentando tras error: {e}");
            Disconnect();
            return Exchange(makeFrame);
        }
    }
}

// MARK: - Configuración inicial

public static class Setup
{
    /// Busca aires en la red local.
    public static Task<List<DiscoveredDevice>> DiscoverAsync(string? target = null, double timeout = 4) =>
        Task.Run(() => UdpDiscovery.Scan(target, timeout));

    /// Inicia sesión en SmartHome, obtiene el token y la key del aire y comprueba que funcionan.
    /// La contraseña solo se usa aquí y no se guarda.
    public static async Task<DeviceConfig> ObtainKeysAsync(DiscoveredDevice device, string account, string password)
    {
        var cloud = new SmartHomeCloud();
        await cloud.LoginAsync(account, password);

        Exception lastError = MideaException.Auth("la cuenta no tiene este aire");
        foreach (var bigEndian in new[] { false, true })
        {
            var udpid = MideaCrypto.UdpId(device.Id, bigEndian);
            try
            {
                var (token, key) = await cloud.TokenAsync(udpid, device.Id);
                var config = new DeviceConfig(device) { Token = token, Key = key };
                using var ac = new MideaAC(config);
                await ac.AuthenticateAsync();
                return config;
            }
            catch (Exception e) { lastError = e; }
        }
        throw lastError;
    }
}
