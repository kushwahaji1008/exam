using System.Security.Cryptography;
using System.Text;

namespace AuthService.Services;

/// <summary>
/// RFC 4648 Base32 and RFC 6238 TOTP (HMAC-SHA1, 30s period, 6 digits) helper.
/// </summary>
public static class TotpHelper
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private const int DefaultPeriodSeconds = 30;
    private const int DefaultDigits = 6;

    public static string GenerateBase32Secret(int byteLength = 20)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteLength);
        return ToBase32(bytes);
    }

    public static string ToBase32(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return string.Empty;

        var result = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = data[0];
        int next = 1;
        int bitsLeft = 8;

        while (bitsLeft > 0 || next < data.Length)
        {
            if (bitsLeft < 5)
            {
                if (next < data.Length)
                {
                    buffer <<= 8;
                    buffer |= data[next++] & 0xFF;
                    bitsLeft += 8;
                }
                else
                {
                    int pad = 5 - bitsLeft;
                    buffer <<= pad;
                    bitsLeft += pad;
                }
            }

            int index = 0x1F & (buffer >> (bitsLeft - 5));
            bitsLeft -= 5;
            result.Append(Base32Alphabet[index]);
        }

        return result.ToString();
    }

    public static byte[] FromBase32(string base32)
    {
        if (string.IsNullOrWhiteSpace(base32))
            throw new ArgumentException("Base32 secret cannot be empty.", nameof(base32));

        var cleaned = base32.Trim().TrimEnd('=').Replace(" ", "").Replace("-", "").ToUpperInvariant();
        if (cleaned.Length == 0)
            throw new ArgumentException("Base32 secret contains no valid characters.", nameof(base32));

        var bytes = new List<byte>(cleaned.Length * 5 / 8);
        int buffer = 0;
        int bitsLeft = 0;

        foreach (char c in cleaned)
        {
            int val = Base32Alphabet.IndexOf(c);
            if (val < 0)
                throw new FormatException($"Invalid Base32 character '{c}'.");

            buffer = (buffer << 5) | val;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                bytes.Add((byte)((buffer >> (bitsLeft - 8)) & 0xFF));
                bitsLeft -= 8;
            }
        }

        return bytes.ToArray();
    }

    public static string ComputeCode(string base32Secret, DateTime utcNow, int periodSeconds = DefaultPeriodSeconds)
    {
        var key = FromBase32(base32Secret);
        long counter = new DateTimeOffset(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc)).ToUnixTimeSeconds() / periodSeconds;
        return ComputeHotp(key, counter);
    }

    public static bool VerifyCode(string? base32Secret, string? code, DateTime? utcNow = null, int allowedDriftWindows = 1)
    {
        if (string.IsNullOrWhiteSpace(base32Secret) || string.IsNullOrWhiteSpace(code))
            return false;

        var normalizedCode = code.Trim();
        if (normalizedCode.Length != DefaultDigits || !normalizedCode.All(char.IsDigit))
            return false;

        byte[] key;
        try
        {
            key = FromBase32(base32Secret);
        }
        catch
        {
            return false;
        }

        var now = DateTime.SpecifyKind(utcNow ?? DateTime.UtcNow, DateTimeKind.Utc);
        long currentCounter = new DateTimeOffset(now).ToUnixTimeSeconds() / DefaultPeriodSeconds;
        var candidateBytes = Encoding.ASCII.GetBytes(normalizedCode);

        bool matched = false;
        for (int offset = -allowedDriftWindows; offset <= allowedDriftWindows; offset++)
        {
            var expected = ComputeHotp(key, currentCounter + offset);
            var expectedBytes = Encoding.ASCII.GetBytes(expected);
            if (CryptographicOperations.FixedTimeEquals(candidateBytes, expectedBytes))
            {
                matched = true;
            }
        }

        return matched;
    }

    private static string ComputeHotp(byte[] key, long counter)
    {
        Span<byte> counterBytes = stackalloc byte[8];
        for (int i = 7; i >= 0; i--)
        {
            counterBytes[i] = (byte)(counter & 0xFF);
            counter >>= 8;
        }

        Span<byte> hash = stackalloc byte[20];
        using var hmac = new HMACSHA1(key);
        hmac.TryComputeHash(counterBytes, hash, out _);

        int offset = hash[hash.Length - 1] & 0x0F;
        int binary = ((hash[offset] & 0x7F) << 24)
                   | ((hash[offset + 1] & 0xFF) << 16)
                   | ((hash[offset + 2] & 0xFF) << 8)
                   | (hash[offset + 3] & 0xFF);

        int otp = binary % 1_000_000;
        return otp.ToString("D6");
    }
}
