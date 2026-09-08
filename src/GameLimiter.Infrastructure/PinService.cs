using System.Security.Cryptography;
namespace GameLimiter.Infrastructure;

public static class PinService
{
    public static string Hash(string pin)
    {
        if (pin.Length is < 4 or > 12 || !pin.All(char.IsAsciiDigit)) throw new ArgumentException("PIN должен содержать от 4 до 12 цифр.");
        var salt = RandomNumberGenerator.GetBytes(16);
        return Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(pin, salt, 600_000, HashAlgorithmName.SHA256, 32));
    }
    public static bool Verify(string pin, string? stored)
    {
        if (stored is null || pin.Length > 12) return false;
        try { var parts = stored.Split(':'); return parts.Length == 2 && CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(parts[1]), Rfc2898DeriveBytes.Pbkdf2(pin, Convert.FromBase64String(parts[0]), 600_000, HashAlgorithmName.SHA256, 32)); }
        catch (FormatException) { return false; }
    }
}
