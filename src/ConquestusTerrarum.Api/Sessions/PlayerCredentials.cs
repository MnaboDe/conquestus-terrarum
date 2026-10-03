using System.Security.Cryptography;
using System.Text;

namespace ConquestusTerrarum.Api.Sessions;

public static class PlayerCredentials
{
    private const string JoinAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string GenerateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] HashToken(string token) => SHA256.HashData(Encoding.ASCII.GetBytes(token));

    public static bool IsValidTokenFormat(string token) => token.Length == 43 &&
        token.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_');

    public static string GenerateJoinCode() => new(Enumerable.Range(0, 6)
        .Select(_ => JoinAlphabet[RandomNumberGenerator.GetInt32(JoinAlphabet.Length)]).ToArray());

    public static string? NormalizeJoinCode(string? code)
    {
        var normalized = code?.Trim().ToUpperInvariant();
        return normalized is { Length: 6 } && normalized.All(JoinAlphabet.Contains) ? normalized : null;
    }
}
