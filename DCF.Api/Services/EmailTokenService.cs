using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace DCF.Api.Services;

public class EmailTokenService(IOptions<EmailOptions> options)
{
    public string GenerateToken(Guid userId)
    {
        var id = userId.ToString("N");
        var hmac = ComputeHmac(id, options.Value.UnsubscribeSecret);

        return $"{id}:{hmac}";
    }

    public Guid? ValidateToken(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        var colonIndex = token.IndexOf(':');

        if (colonIndex < 0)
        {
            return null;
        }

        var id = token[..colonIndex];
        var providedHmac = token[(colonIndex + 1)..];

        if (!Guid.TryParseExact(id, "N", out var userId))
        {
            return null;
        }

        var expectedHmac = ComputeHmac(id, options.Value.UnsubscribeSecret);

        try
        {
            var expectedBytes = Convert.FromBase64String(PadBase64(expectedHmac));
            var providedBytes = Convert.FromBase64String(PadBase64(providedHmac));

            if (!CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes))
            {
                return null;
            }
        }
        catch (FormatException)
        {
            return null;
        }

        return userId;
    }

    private static string ComputeHmac(string data, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var dataBytes = Encoding.UTF8.GetBytes(data);
        var hash = HMACSHA256.HashData(keyBytes, dataBytes);

        return Convert.ToBase64String(hash)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string PadBase64(string base64Url)
    {
        var s = base64Url.Replace('-', '+').Replace('_', '/');

        return s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
    }
}
