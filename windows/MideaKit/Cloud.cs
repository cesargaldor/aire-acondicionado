// Acceso mínimo a la nube SmartHome de Midea: solo para obtener el token y la key una vez
// (port de Cloud.swift).

using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MideaKit;

public sealed class SmartHomeCloud
{
    private const string BaseUrl = "https://mp-prod.appsmb.com";
    private const string AppId = "1010";
    private static readonly byte[] HmacKey = Encoding.ASCII.GetBytes("PROD_VnoClJI9aikS8dyy");
    private const string IotKey = "meicloud";
    private const string LoginKey = "ac21b9f9cbfe4ca5a88562ef25e2b768";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly string _deviceId = Hex.Encode(MideaCrypto.Random(8));
    private string _accessToken = "";

    // MARK: Firmas (iguales que msmart-ng)

    public static string Sign(string json, string random) =>
        Hex.Encode(MideaCrypto.HmacSha256(HmacKey, Encoding.UTF8.GetBytes(IotKey + json + random)));

    public static string EncryptPassword(string loginId, string password)
    {
        var m1 = Hex.Encode(MideaCrypto.Sha256(Encoding.UTF8.GetBytes(password)));
        return Hex.Encode(MideaCrypto.Sha256(Encoding.UTF8.GetBytes(loginId + m1 + LoginKey)));
    }

    public static string EncryptIamPassword(string loginId, string password)
    {
        var m1 = Hex.Encode(MideaCrypto.Md5(Encoding.UTF8.GetBytes(password)));
        var m2 = Hex.Encode(MideaCrypto.Md5(Encoding.UTF8.GetBytes(m1)));
        return Hex.Encode(MideaCrypto.Sha256(Encoding.UTF8.GetBytes(loginId + m2 + LoginKey)));
    }

    // MARK: Peticiones

    private static string Stamp() => DateTime.UtcNow.ToString("yyyyMMddHHmmss");

    private JsonObject BaseBody(params (string Key, JsonNode? Value)[] extra)
    {
        var body = new JsonObject
        {
            ["appId"] = AppId,
            ["src"] = AppId,
            ["format"] = 2,
            ["clientType"] = 1,
            ["language"] = "en_US",
            ["deviceId"] = _deviceId,
            ["stamp"] = Stamp(),
            ["reqId"] = Hex.Encode(MideaCrypto.Random(16)),
        };
        foreach (var (k, v) in extra) body[k] = v;
        return body;
    }

    private async Task<JsonObject> RequestAsync(string endpoint, JsonObject body)
    {
        var json = body.ToJsonString();
        var random = Hex.Encode(MideaCrypto.Random(16));
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/mas/v5/app/proxy?alias={endpoint}")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("secretVersion", "1");
        req.Headers.Add("sign", Sign(json, random));
        req.Headers.Add("random", random);
        req.Headers.Add("accessToken", _accessToken);

        HttpResponseMessage resp;
        try { resp = await Http.SendAsync(req); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            throw MideaException.Net("no se pudo contactar con la nube de Midea");
        }
        using (resp)
        {
            if (!resp.IsSuccessStatusCode) throw MideaException.Net("la nube respondió con error HTTP");
            var text = await resp.Content.ReadAsStringAsync();
            JsonObject obj;
            try { obj = JsonNode.Parse(text) as JsonObject ?? throw new JsonException(); }
            catch (JsonException) { throw new MideaException(MideaErrorKind.Cloud, "respuesta no válida", -1); }

            var codeNode = obj["code"];
            var code = -1;
            if (codeNode is JsonValue v)
            {
                if (v.TryGetValue<int>(out var n)) code = n;
                else if (v.TryGetValue<string>(out var s) && int.TryParse(s, out var p)) code = p;
            }
            if (code != 0)
                throw new MideaException(MideaErrorKind.Cloud, obj["msg"]?.GetValue<string>() ?? "error desconocido", code);
            return obj["data"] as JsonObject ?? [];
        }
    }

    // MARK: API

    public async Task LoginAsync(string account, string password)
    {
        var id = await RequestAsync("/v1/user/login/id/get", BaseBody(("loginAccount", account)));
        var loginId = id["loginId"]?.GetValue<string>() ?? throw new MideaException(MideaErrorKind.Cloud, "no se recibió loginId", -1);

        var body = new JsonObject
        {
            ["data"] = new JsonObject { ["platform"] = 2, ["deviceId"] = _deviceId },
            ["iotData"] = new JsonObject
            {
                ["appId"] = AppId,
                ["src"] = AppId,
                ["clientType"] = 1,
                ["loginAccount"] = account,
                ["iampwd"] = EncryptIamPassword(loginId, password),
                ["password"] = EncryptPassword(loginId, password),
                ["pushToken"] = Hex.Encode(MideaCrypto.Random(90)),
                ["stamp"] = Stamp(),
                ["reqId"] = Hex.Encode(MideaCrypto.Random(16)),
            },
        };
        var response = await RequestAsync("/mj/user/login", body);
        _accessToken = response["mdata"]?["accessToken"]?.GetValue<string>()
            ?? throw new MideaException(MideaErrorKind.Cloud, "no se recibió accessToken", -1);
    }

    public async Task<(string Token, string Key)> TokenAsync(string udpid, ulong applianceId)
    {
        var response = await RequestAsync("/v1/iot/secure/getToken",
            BaseBody(("udpid", udpid), ("applianceCodes", applianceId.ToString())));
        if (response["tokenlist"] is JsonArray list)
            foreach (var item in list)
                if (string.Equals(item?["udpId"]?.GetValue<string>(), udpid, StringComparison.OrdinalIgnoreCase)
                    && item?["token"]?.GetValue<string>() is { } t && item["key"]?.GetValue<string>() is { } k)
                    return (t, k);
        throw new MideaException(MideaErrorKind.Cloud, "la cuenta no tiene claves para este aire", -1);
    }
}

public static class ConfigImport
{
    /// Lee un config.json en formato {"ip", "id", "token", "key"} (el de msmart-ng y similares).
    public static DeviceConfig? Parse(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject obj || obj["ip"]?.GetValue<string>() is not { } ip) return null;
            ulong id;
            var idNode = obj["id"] as JsonValue;
            if (idNode is null) return null;
            if (idNode.TryGetValue<ulong>(out var n)) id = n;
            else if (idNode.TryGetValue<string>(out var s) && ulong.TryParse(s, out var p)) id = p;
            else return null;
            var token = (obj["token"] as JsonValue)?.TryGetValue<string>(out var t) == true ? t : null;
            var key = (obj["key"] as JsonValue)?.TryGetValue<string>(out var k) == true ? k : null;
            // El puerto de un config.json suele ser el de la web (8080); el aire usa el 6444
            return new DeviceConfig
            {
                Ip = ip,
                Port = 6444,
                Id = id,
                Version = token is not null && key is not null ? 3 : 2,
                Token = token,
                Key = key,
            };
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
