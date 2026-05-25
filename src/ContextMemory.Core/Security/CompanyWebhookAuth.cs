using System.Security.Cryptography;
using System.Text;

namespace ContextMemory.Core.Security;

public static class CompanyWebhookAuth
{
    public const string SignatureHeader = "X-Company-Signature";

    public static string CreateWebhookSecret() =>
        $"cm_wh_{RandomNumberGenerator.GetHexString(24, lowercase: true)}";

    public static string ComputeSignature(string secret, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        return $"sha256={Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    public static bool Validate(string secret, string body, string? signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(signatureHeader))
            return false;

        var expected = ComputeSignature(secret, body);
        var provided = signatureHeader.Trim();
        return expected.Length == provided.Length
            && CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected),
                Encoding.UTF8.GetBytes(provided));
    }
}
