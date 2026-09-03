using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace DovahLink.Host.Trust;

/// <summary>
/// Pure, stateless security-primitive helpers for hashing a pairing credential before it enters
/// durable trust state and for comparing presented secrets in constant time. Shared by pairing
/// (issuing and committing a credential) and hello authentication (verifying a presented
/// <c>trusted_device_credential</c> against a stored <see cref="TrustRecord.CredentialVerifier"/>),
/// so credential verification has exactly one implementation rather than two independently
/// maintained copies of the same security-sensitive comparison.
/// </summary>
public static class CredentialHasher
{
    /// <summary>Hashes a credential before it enters durable trust state, or to verify one presented later.</summary>
    /// <param name="credential">The raw credential value to hash.</param>
    /// <returns>A hex-encoded, one-way SHA-256 hash of <paramref name="credential"/>.</returns>
    public static string Hash(string credential) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(credential)));

    /// <summary>Compares two values in constant time, without an early-exit equality check.</summary>
    /// <param name="expected">The known-correct value.</param>
    /// <param name="presented">The value presented for comparison.</param>
    /// <returns><see langword="true"/> when both values are byte-for-byte equal.</returns>
    public static bool FixedTimeEquals(string expected, string presented) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(presented));

    /// <summary>
    /// Reports whether <paramref name="value"/> is a well-formed presented credential: exactly
    /// <paramref name="expectedHexLength"/> hex characters (either case), matching the shape
    /// <c>protocol/schema/README.md</c>'s <c>hello</c> section requires for
    /// <c>trusted_device_credential</c>'s <c>auth.token</c>. A caller rejects a value that fails this
    /// check as malformed protocol input before it ever reaches <see cref="Hash"/> or
    /// <see cref="FixedTimeEquals"/>, per <c>ai/context/protocol/security.md</c>'s requirement that
    /// malformed authentication input be rejected before authentication services.
    /// </summary>
    /// <param name="value">The presented credential to validate.</param>
    /// <param name="expectedHexLength">The exact number of hex characters a well-formed credential carries.</param>
    public static bool IsValidHexCredential(string? value, int expectedHexLength) =>
        value is not null && value.Length == expectedHexLength && value.All(Uri.IsHexDigit);
}
