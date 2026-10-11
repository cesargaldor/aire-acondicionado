// Sockets: difusión UDP para encontrar el aire y TCP para hablar con él (port de Transport.swift).

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MideaKit;

public static class UdpDiscovery
{
    /// Direcciones de difusión de cada tarjeta de red activa (en Windows 255.255.255.255
    /// solo sale por una de ellas, así que también se envía a la de cada subred).
    private static IEnumerable<IPAddress> BroadcastTargets()
    {
        yield return IPAddress.Broadcast;
        IEnumerable<UnicastIPAddressInformation> addresses;
        try
        {
            addresses = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                .ToList();
        }
        catch (NetworkInformationException) { yield break; }

        foreach (var a in addresses)
        {
            var ip = a.Address.GetAddressBytes();
            var mask = a.IPv4Mask?.GetAddressBytes();
            if (mask is null || mask.Length != 4 || mask.All(b => b == 0)) continue;
            var bcast = new byte[4];
            for (var i = 0; i < 4; i++) bcast[i] = (byte)(ip[i] | ~mask[i]);
            yield return new IPAddress(bcast);
        }
    }

    /// Envía el mensaje de descubrimiento (a toda la red o a una IP) y recoge las respuestas.
    public static List<DiscoveredDevice> Scan(string? target = null, double timeout = 4)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.EnableBroadcast = true;
        socket.Bind(new IPEndPoint(IPAddress.Any, 0));

        var targets = target is null
            ? BroadcastTargets().Distinct().ToList()
            : [IPAddress.Parse(target)];
        foreach (var port in new[] { 6445, 20086 })
            foreach (var t in targets)
                for (var i = 0; i < 3; i++)
                {
                    try { socket.SendTo(Discovery.Message, new IPEndPoint(t, port)); }
                    catch (SocketException) { /* una tarjeta sin ruta no debe parar la búsqueda */ }
                }

        var found = new Dictionary<string, DiscoveredDevice>();
        var deadline = DateTime.UtcNow.AddSeconds(timeout);
        var buffer = new byte[2048];
        while (DateTime.UtcNow < deadline)
        {
            var remaining = (int)Math.Max(1, (deadline - DateTime.UtcNow).TotalMilliseconds);
            if (!socket.Poll(Math.Min(remaining, 500) * 1000, SelectMode.SelectRead)) continue;
            EndPoint from = new IPEndPoint(IPAddress.Any, 0);
            int n;
            try { n = socket.ReceiveFrom(buffer, ref from); }
            catch (SocketException) { continue; }
            var ip = ((IPEndPoint)from).Address.ToString();
            if (n <= 0 || found.ContainsKey(ip)) continue;
            try
            {
                found[ip] = Discovery.Parse(buffer[..n], ip);
                if (target is not null) break;
            }
            catch (MideaException) { }
        }
        return found.Values.OrderBy(d => d.Ip).ToList();
    }
}

public sealed class TcpConnection : IDisposable
{
    private Socket? _socket;

    public bool IsOpen => _socket is not null;

    public void Connect(string ip, int port, double timeout = 5)
    {
        Close();
        if (!IPAddress.TryParse(ip, out var address)) throw MideaException.Net($"IP no válida: {ip}");
        var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            var result = s.BeginConnect(new IPEndPoint(address, port), null, null);
            if (!result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(timeout)))
            {
                s.Close();
                throw MideaException.Timeout();
            }
            s.EndConnect(result);
        }
        catch (SocketException e)
        {
            s.Close();
            throw MideaException.Net($"no se pudo conectar con {ip}: {e.Message}");
        }
        _socket = s;
    }

    public void Send(byte[] data)
    {
        if (_socket is null) throw MideaException.Net("sin conexión");
        try
        {
            var sent = 0;
            while (sent < data.Length) sent += _socket.Send(data, sent, data.Length - sent, SocketFlags.None);
        }
        catch (SocketException)
        {
            Close();
            throw MideaException.Net("conexión cerrada al enviar");
        }
    }

    /// Lee lo que llegue en como mucho `timeout` segundos. Devuelve vacío si no llega nada.
    public byte[] Receive(double timeout)
    {
        if (_socket is null) throw MideaException.Net("sin conexión");
        try
        {
            if (!_socket.Poll((int)(Math.Max(0, timeout) * 1_000_000), SelectMode.SelectRead)) return [];
            var buffer = new byte[4096];
            var n = _socket.Receive(buffer);
            if (n <= 0)
            {
                Close();
                throw MideaException.Net("el aire cerró la conexión");
            }
            return buffer[..n];
        }
        catch (SocketException)
        {
            Close();
            throw MideaException.Net("el aire cerró la conexión");
        }
    }

    public void Close()
    {
        try { _socket?.Close(); } catch { }
        _socket = null;
    }

    public void Dispose() => Close();
}
