using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace GirlfriendPanel.api.Security;

public static class TokenSigner
{
    public static string CreateToken<T>(T payload, string secret)
    {
        var json = JsonSerializer.Serialize(payload);
        var payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(json));
        var sigB64 = Base64UrlEncode(HmacSha256(payloadB64, secret));
        return $"{payloadB64}.{sigB64}";
    }

    public static T? VerifyToken<T>(string token, string secret)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 2) return default;

            var payloadB64 = parts[0];
            var sigB64 = parts[1];

            var expectedSig = Base64UrlEncode(HmacSha256(payloadB64, secret));
            if (!ConstantTimeEquals(sigB64, expectedSig)) return default;

            var json = Encoding.UTF8.GetString(Base64UrlDecode(payloadB64));
            return JsonSerializer.Deserialize<T>(json);
        }
        catch
        {
            return default;
        }
    }


    private static byte[] HmacSha256(string payloadB64, string secret)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadB64));
    }

    private static bool ConstantTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }

    private static string Base64UrlEncode(byte[] data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }
}