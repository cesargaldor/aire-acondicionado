// Cifrado y utilidades de bytes usadas por el protocolo de Midea (port de MideaKit/Crypto.swift).

using System.Security.Cryptography;
using System.Text;

namespace MideaKit;

public enum MideaErrorKind { Crypto, Protocol, Authentication, Timeout, Network, Cloud, NotFound }

public sealed class MideaException : Exception
{
    public MideaErrorKind Kind { get; }
    public int Code { get; }

    public MideaException(MideaErrorKind kind, string message, int code = 0) : base(message)
    {
        Kind = kind;
        Code = code;
    }

    public override string ToString() => Kind switch
    {
        MideaErrorKind.Crypto => "Error de cifrado",
        MideaErrorKind.Protocol => $"Error de protocolo: {Message}",
        MideaErrorKind.Authentication => $"Error de autenticación: {Message}",
        MideaErrorKind.Timeout => "El aire no responde",
        MideaErrorKind.Network => $"Error de red: {Message}",
        MideaErrorKind.Cloud => $"Nube de Midea ({Code}): {Message}",
        MideaErrorKind.NotFound => "No se encontró ningún aire en la red",
        _ => Message,
    };

    public static MideaException Proto(string m) => new(MideaErrorKind.Protocol, m);
    public static MideaException Auth(string m) => new(MideaErrorKind.Authentication, m);
    public static MideaException Net(string m) => new(MideaErrorKind.Network, m);
    public static MideaException Timeout() => new(MideaErrorKind.Timeout, "sin respuesta");
}

public static class Hex
{
    public static string Encode(ReadOnlySpan<byte> data) => Convert.ToHexString(data).ToLowerInvariant();

    public static byte[]? TryDecode(string? hex)
    {
        if (string.IsNullOrEmpty(hex) || hex.Length % 2 != 0) return hex == "" ? [] : null;
        try { return Convert.FromHexString(hex); } catch (FormatException) { return null; }
    }
}

public static class MideaCrypto
{
    /// Clave de firma de los paquetes V2 y clave AES derivada de ella.
    public static readonly byte[] SignKey = Encoding.ASCII.GetBytes("xhdiwjnchekd4d512chdjx5d8e4c394D2D7S");
    public static readonly byte[] EncKey = MD5.HashData(SignKey);

    public static byte[] Sha256(ReadOnlySpan<byte> data) => SHA256.HashData(data);
    public static byte[] Md5(ReadOnlySpan<byte> data) => MD5.HashData(data);
    public static byte[] HmacSha256(byte[] key, byte[] message) => HMACSHA256.HashData(key, message);

    public static byte[] Xor(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        var n = Math.Min(a.Length, b.Length);
        var r = new byte[n];
        for (var i = 0; i < n; i++) r[i] = (byte)(a[i] ^ b[i]);
        return r;
    }

    public static byte[] Random(int count) => RandomNumberGenerator.GetBytes(count);

    private static Aes NewAes(byte[] key)
    {
        var aes = Aes.Create();
        aes.Key = key;
        return aes;
    }

    /// AES-ECB con relleno PKCS7 y la clave fija de Midea (tramas dentro del paquete V2).
    public static byte[] EncryptEcb(byte[] data)
    {
        using var aes = NewAes(EncKey);
        return aes.EncryptEcb(data, PaddingMode.PKCS7);
    }

    public static byte[] DecryptEcb(byte[] data)
    {
        if (data.Length == 0 || data.Length % 16 != 0) throw new MideaException(MideaErrorKind.Crypto, "longitud");
        using var aes = NewAes(EncKey);
        try { return aes.DecryptEcb(data, PaddingMode.PKCS7); }
        catch (CryptographicException) { throw new MideaException(MideaErrorKind.Crypto, "relleno"); }
    }

    /// AES-CBC con IV a cero y sin relleno (capa V3).
    public static byte[] EncryptCbc(byte[] key, byte[] data)
    {
        if (data.Length % 16 != 0) throw new MideaException(MideaErrorKind.Crypto, "longitud");
        if (data.Length == 0) return [];
        using var aes = NewAes(key);
        return aes.EncryptCbc(data, new byte[16], PaddingMode.None);
    }

    public static byte[] DecryptCbc(byte[] key, byte[] data)
    {
        if (data.Length % 16 != 0) throw new MideaException(MideaErrorKind.Crypto, "longitud");
        if (data.Length == 0) return [];
        using var aes = NewAes(key);
        return aes.DecryptCbc(data, new byte[16], PaddingMode.None);
    }

    /// Firma MD5 de los paquetes V2.
    public static byte[] Sign(ReadOnlySpan<byte> data) => Md5([.. data, .. SignKey]);

    /// Identificador UDP que la nube usa para buscar el token de un aire.
    public static byte[] UdpId(byte[] deviceId)
    {
        var h = Sha256(deviceId);
        return Xor(h.AsSpan(0, 16), h.AsSpan(16, 16));
    }

    public static string UdpId(ulong deviceId, bool bigEndian)
    {
        var le = new byte[6];
        for (var i = 0; i < 6; i++) le[i] = (byte)(deviceId >> (8 * i));
        if (bigEndian) Array.Reverse(le);
        return Hex.Encode(UdpId(le));
    }
}
