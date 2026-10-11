// Dónde guarda Aire sus cosas en Windows.
//
// - La configuración del aire (con el token y la key) va cifrada con DPAPI, ligada a tu usuario de
//   Windows: es el equivalente al Llavero del Mac. Otro usuario u otro PC no pueden descifrarla.
// - Los ajustes (pitido) van en un JSON normal.
// - "Iniciar con Windows" usa la clave Run del registro de tu usuario.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using MideaKit;

namespace Aire;

internal static class Storage
{
    private static readonly string Folder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aire");
    private static string DevicePath => Path.Combine(Folder, "device.dat");
    private static string SettingsPath => Path.Combine(Folder, "settings.json");

    // MARK: Configuración del aire (cifrada)

    public static DeviceConfig? LoadDevice()
    {
        try
        {
            if (!File.Exists(DevicePath)) return null;
            var plain = Dpapi.Unprotect(File.ReadAllBytes(DevicePath));
            return JsonSerializer.Deserialize<DeviceConfig>(Encoding.UTF8.GetString(plain));
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static bool SaveDevice(DeviceConfig config)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(config));
            File.WriteAllBytes(DevicePath, Dpapi.Protect(json));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void DeleteDevice()
    {
        try { File.Delete(DevicePath); } catch (Exception) { }
    }

    // MARK: Ajustes

    private sealed class SettingsFile
    {
        public bool Beep { get; set; } = true;
    }

    public static bool Beep
    {
        get
        {
            try
            {
                if (File.Exists(SettingsPath))
                    return JsonSerializer.Deserialize<SettingsFile>(File.ReadAllText(SettingsPath))?.Beep ?? true;
            }
            catch (Exception) { }
            return true;
        }
        set
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new SettingsFile { Beep = value }));
            }
            catch (Exception) { }
        }
    }

    // MARK: Iniciar con Windows

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool LaunchAtLogin
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue("Aire") is string;
            }
            catch (Exception) { return false; }
        }
        set
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (value && Environment.ProcessPath is { } exe) key.SetValue("Aire", $"\"{exe}\"");
                else key.DeleteValue("Aire", false);
            }
            catch (Exception) { }
        }
    }
}

/// DPAPI (CryptProtectData) llamada directamente, sin dependencias extra.
internal static class Dpapi
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob dataIn, string? description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, int flags, ref DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, int flags, ref DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr mem);

    private const int UiForbidden = 0x1;

    public static byte[] Protect(byte[] data) => Run(data, true);
    public static byte[] Unprotect(byte[] data) => Run(data, false);

    private static byte[] Run(byte[] data, bool protect)
    {
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        var input = new DataBlob { cbData = data.Length, pbData = handle.AddrOfPinnedObject() };
        var output = new DataBlob();
        try
        {
            var ok = protect
                ? CryptProtectData(ref input, "Aire", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output);
            if (!ok) throw new InvalidOperationException($"DPAPI falló ({Marshal.GetLastWin32Error()})");
            var result = new byte[output.cbData];
            Marshal.Copy(output.pbData, result, 0, output.cbData);
            return result;
        }
        finally
        {
            handle.Free();
            if (output.pbData != IntPtr.Zero) LocalFree(output.pbData);
        }
    }
}
