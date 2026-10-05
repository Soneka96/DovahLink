using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace DovahLink.Host.Identity;

/// <summary>
/// A validated ECDSA P-256 public key held as its exact canonical DER SubjectPublicKeyInfo: 91 bytes,
/// id-ecPublicKey with the named secp256r1 curve, and an uncompressed point on the curve. The full
/// SubjectPublicKeyInfo is the key's public identity; <see cref="Fingerprint"/> is display and index
/// metadata derived from it, never a substitute for it.
/// </summary>
public sealed class P256PublicKey : IEquatable<P256PublicKey>
{
    /// <summary>The exact canonical SubjectPublicKeyInfo bytes, owned by this instance.</summary>
    private readonly byte[] subjectPublicKeyInfo;

    /// <summary>Wraps already-validated SubjectPublicKeyInfo bytes this instance takes ownership of.</summary>
    /// <param name="subjectPublicKeyInfo">The validated canonical SubjectPublicKeyInfo.</param>
    private P256PublicKey(byte[] subjectPublicKeyInfo)
    {
        this.subjectPublicKeyInfo = subjectPublicKeyInfo;
        Fingerprint = Base64Url.EncodeToString(SHA256.HashData(subjectPublicKeyInfo));
    }

    /// <summary>The exact canonical 91-byte DER SubjectPublicKeyInfo.</summary>
    public ReadOnlySpan<byte> SubjectPublicKeyInfo => subjectPublicKeyInfo;

    /// <summary>The unpadded base64url SHA-256 of <see cref="SubjectPublicKeyInfo"/>, for display and indexing only.</summary>
    public string Fingerprint { get; }

    /// <summary>Validates and copies a canonical P-256 SubjectPublicKeyInfo.</summary>
    /// <param name="subjectPublicKeyInfo">The candidate DER SubjectPublicKeyInfo bytes.</param>
    /// <returns>The validated key.</returns>
    /// <exception cref="ArgumentException">The bytes are not exactly one canonical P-256 SubjectPublicKeyInfo.</exception>
    public static P256PublicKey FromSubjectPublicKeyInfo(ReadOnlySpan<byte> subjectPublicKeyInfo) =>
        TryFromSubjectPublicKeyInfo(subjectPublicKeyInfo, out P256PublicKey? key)
            ? key
            : throw new ArgumentException(
                "The value is not a canonical uncompressed P-256 SubjectPublicKeyInfo.", nameof(subjectPublicKeyInfo));

    /// <summary>Validates and copies a canonical P-256 SubjectPublicKeyInfo without throwing.</summary>
    /// <param name="subjectPublicKeyInfo">The candidate DER SubjectPublicKeyInfo bytes.</param>
    /// <param name="key">The validated key, or <see langword="null"/> when validation fails.</param>
    /// <returns>Whether the bytes are exactly one canonical P-256 SubjectPublicKeyInfo.</returns>
    public static bool TryFromSubjectPublicKeyInfo(ReadOnlySpan<byte> subjectPublicKeyInfo, [NotNullWhen(true)] out P256PublicKey? key)
    {
        key = null;
        if (subjectPublicKeyInfo.Length != Constants.P256SubjectPublicKeyInfoLength ||
            !subjectPublicKeyInfo.StartsWith(Constants.P256SubjectPublicKeyInfoPrefix))
        {
            return false;
        }

        // The fixed prefix pins the algorithm, curve, and point form; the platform import then rejects
        // a point that is not on the curve (Windows reports that as an unsupported curve parameter).
        try
        {
            using ECDsa imported = ECDsa.Create();
            imported.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out int bytesRead);
            if (bytesRead != subjectPublicKeyInfo.Length)
            {
                return false;
            }
        }
        catch (Exception exception) when (exception is CryptographicException or PlatformNotSupportedException)
        {
            return false;
        }

        key = new P256PublicKey(subjectPublicKeyInfo.ToArray());
        return true;
    }

    /// <inheritdoc/>
    public bool Equals(P256PublicKey? other) =>
        other is not null && subjectPublicKeyInfo.AsSpan().SequenceEqual(other.subjectPublicKeyInfo);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as P256PublicKey);

    /// <inheritdoc/>
    public override int GetHashCode() => Fingerprint.GetHashCode(StringComparison.Ordinal);

    /// <inheritdoc/>
    public override string ToString() => Fingerprint;
}
