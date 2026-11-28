using System.Security.Cryptography;
using System.Text;

namespace StorageService.Services;

public sealed class MediaTicketService
{
    private readonly byte[] _signingKey;

    public MediaTicketService(IConfiguration configuration)
    {
        var key = configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is required for media ticket signing.");
        _signingKey = SHA256.HashData(Encoding.UTF8.GetBytes($"media-stream-v1:{key}"));
    }

    public string CreateSignedStreamUrl(string rawUrl, string userId, TimeSpan? ttl = null)
    {
        if (!TryParseLocalDownloadPath(rawUrl, out var folder, out var fileName))
            return rawUrl;

        var expiresAt = DateTimeOffset.UtcNow.Add(ttl ?? TimeSpan.FromMinutes(15)).ToUnixTimeSeconds();
        var payload = $"{folder}|{fileName}|{userId}|{expiresAt}";
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var signature = ComputeSignature(payloadBytes);

        var token = $"{Base64UrlEncode(payloadBytes)}.{Base64UrlEncode(signature)}";
        return $"/api/v1/files/download/{folder}/{fileName}?ticket={Uri.EscapeDataString(token)}";
    }

    public bool ValidateTicket(string? ticket, string expectedFolder, string expectedFileName, out string? userId)
    {
        userId = null;
        if (string.IsNullOrWhiteSpace(ticket)) return false;

        var parts = ticket.Split('.');
        if (parts.Length != 2) return false;

        byte[] payloadBytes;
        byte[] providedSig;
        try
        {
            payloadBytes = Base64UrlDecode(parts[0]);
            providedSig = Base64UrlDecode(parts[1]);
        }
        catch
        {
            return false;
        }

        var expectedSig = ComputeSignature(payloadBytes);
        if (!CryptographicOperations.FixedTimeEquals(providedSig, expectedSig))
            return false;

        var payloadText = Encoding.UTF8.GetString(payloadBytes);
        var segments = payloadText.Split('|');
        if (segments.Length != 4) return false;

        var folder = segments[0];
        var fileName = segments[1];
        var tokenUserId = segments[2];
        if (!long.TryParse(segments[3], out var expUnix)) return false;

        if (!string.Equals(folder, expectedFolder, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.Equals(fileName, expectedFileName, StringComparison.Ordinal)) return false;
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expUnix) return false;

        userId = tokenUserId;
        return true;
    }

    public static bool TryParseLocalDownloadPath(string? rawUrl, out string folder, out string fileName)
    {
        folder = string.Empty;
        fileName = string.Empty;
        if (string.IsNullOrWhiteSpace(rawUrl)) return false;

        var trimmed = rawUrl.Trim();
        var qIdx = trimmed.IndexOf('?', StringComparison.Ordinal);
        if (qIdx >= 0) trimmed = trimmed[..qIdx];

        const string marker = "/api/v1/files/download/";
        var idx = trimmed.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return false;

        var relative = trimmed[(idx + marker.Length)..].Trim('/');
        var parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return false;

        folder = parts[0];
        fileName = parts[1];
        return !string.IsNullOrWhiteSpace(folder) && !string.IsNullOrWhiteSpace(fileName);
    }

    private byte[] ComputeSignature(byte[] payloadBytes)
    {
        using var hmac = new HMACSHA256(_signingKey);
        return hmac.ComputeHash(payloadBytes);
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string input)
    {
        var padded = input.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }
}
