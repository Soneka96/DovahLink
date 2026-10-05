using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using DovahLink.Host.Identity;

namespace DovahLink.Host.Tests.Identity;

/// <summary>Tests canonical P-256 SubjectPublicKeyInfo validation and its display fingerprint.</summary>
public sealed class P256PublicKeyTests
{
    /// <summary>Verifies the published Host, Client, and substitute test keys keep their exact bytes and fingerprints.</summary>
    /// <param name="subjectPublicKeyInfoHex">The test key's canonical SubjectPublicKeyInfo.</param>
    /// <param name="fingerprint">The test key's published unpadded base64url SHA-256 fingerprint.</param>
    [Theory]
    [InlineData(
        "3059301306072a8648ce3d020106082a8648ce3d03010703420004f2422a662eb6e5065e3ea5587ed92dd959deff9b9e4115bb76dcb02abf07144a68e354b81cc01714608a8ecd61f8d9ac453cda8b0d20056623db09859432498a",
        "_lEke2AL0V0gIvTYMZHejeeS60o7OIf5X-njMIR9X9Q")]
    [InlineData(
        "3059301306072a8648ce3d020106082a8648ce3d03010703420004fdf05d25acd08029eabaf4dbafefda88f9df6acc278a88cff9d67934b71e15cbe8be70c2230db3aa58ac0bd5fd14dbb15e6c94a249aee685890665d145a07484",
        "PDHvBJn32NNvAgEbJ9T_8np0awd4y_LIp40mmyM02kw")]
    [InlineData(
        "3059301306072a8648ce3d020106082a8648ce3d030107034200040bb2280b1872c24858a96435ba7cc34a9ec35f479bb1927225a82a9ae9a15fd90d083305f94466576611f5a3033370d93bbb3caaf7f73aec48c81b8898ddec2f",
        "DgrmHGwGRHMiywVXehVoWYccl9apruRHUAvM5ihSIUg")]
    public void FromSubjectPublicKeyInfo_PublishedTestKey_KeepsExactBytesAndFingerprint(string subjectPublicKeyInfoHex, string fingerprint)
    {
        byte[] spki = Fixtures.BuildP256SubjectPublicKeyInfo(subjectPublicKeyInfoHex);

        P256PublicKey key = P256PublicKey.FromSubjectPublicKeyInfo(spki);

        Assert.Equal(91, key.SubjectPublicKeyInfo.Length);
        Assert.True(key.SubjectPublicKeyInfo.SequenceEqual(spki));
        Assert.Equal(fingerprint, key.Fingerprint);
        Assert.Equal(fingerprint, key.ToString());
    }

    /// <summary>Verifies a freshly generated platform key round-trips and its fingerprint is SHA-256 of the exact SPKI.</summary>
    [Fact]
    public void FromSubjectPublicKeyInfo_GeneratedKey_FingerprintIsUnpaddedBase64UrlSha256()
    {
        using ECDsa generated = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] spki = generated.ExportSubjectPublicKeyInfo();

        P256PublicKey key = P256PublicKey.FromSubjectPublicKeyInfo(spki);

        Assert.Equal(Base64Url.EncodeToString(SHA256.HashData(spki)), key.Fingerprint);
        Assert.DoesNotContain('=', key.Fingerprint);
        Assert.Equal(43, key.Fingerprint.Length);
    }

    /// <summary>Verifies the key copies its input, so later changes to the caller's buffer cannot alter it.</summary>
    [Fact]
    public void FromSubjectPublicKeyInfo_CallerMutatesInput_KeyUnchanged()
    {
        byte[] spki = Fixtures.BuildP256SubjectPublicKeyInfo();
        P256PublicKey key = P256PublicKey.FromSubjectPublicKeyInfo(spki);
        string fingerprint = key.Fingerprint;

        spki[^1] ^= 0x01;

        Assert.True(key.SubjectPublicKeyInfo.SequenceEqual(Fixtures.BuildP256SubjectPublicKeyInfo()));
        Assert.Equal(fingerprint, key.Fingerprint);
    }

    /// <summary>Verifies wrong lengths and trailing bytes are rejected.</summary>
    /// <param name="length">The length to cut or extend the Host test key to.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(27)]
    [InlineData(90)]
    [InlineData(92)]
    public void TryFromSubjectPublicKeyInfo_WrongLength_Rejects(int length)
    {
        byte[] spki = Fixtures.BuildP256SubjectPublicKeyInfo();
        byte[] resized = new byte[length];
        spki.AsSpan(0, Math.Min(length, spki.Length)).CopyTo(resized);

        Assert.False(P256PublicKey.TryFromSubjectPublicKeyInfo(resized, out P256PublicKey? key));
        Assert.Null(key);
        Assert.Throws<ArgumentException>(() => P256PublicKey.FromSubjectPublicKeyInfo(resized));
    }

    /// <summary>Verifies the compressed-point SPKI of the same key is not the canonical form (vector V15).</summary>
    [Fact]
    public void TryFromSubjectPublicKeyInfo_CompressedPoint_Rejects()
    {
        byte[] compressed = Convert.FromHexString(
            "3039301306072a8648ce3d020106082a8648ce3d03010703220002fdf05d25acd08029eabaf4dbafefda88f9df6acc278a88cff9d67934b71e15cb");

        Assert.False(P256PublicKey.TryFromSubjectPublicKeyInfo(compressed, out _));
    }

    /// <summary>Verifies a key on another curve is rejected.</summary>
    [Fact]
    public void TryFromSubjectPublicKeyInfo_P384Key_Rejects()
    {
        using ECDsa p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);

        Assert.False(P256PublicKey.TryFromSubjectPublicKeyInfo(p384.ExportSubjectPublicKeyInfo(), out _));
    }

    /// <summary>Verifies a P-256 key encoded with explicit curve parameters instead of the named curve is rejected.</summary>
    [Fact]
    public void TryFromSubjectPublicKeyInfo_ExplicitCurveParameters_Rejects()
    {
        using ECDsa named = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using ECDsa explicitCurve = ECDsa.Create();
        explicitCurve.ImportParameters(named.ExportExplicitParameters(includePrivateParameters: false));

        byte[] spki = explicitCurve.ExportSubjectPublicKeyInfo();

        Assert.NotEqual(91, spki.Length);
        Assert.False(P256PublicKey.TryFromSubjectPublicKeyInfo(spki, out _));
    }

    /// <summary>Verifies a single changed byte in the fixed algorithm and curve prefix is rejected.</summary>
    /// <param name="index">The prefix byte to change.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    [InlineData(22)]
    [InlineData(26)]
    public void TryFromSubjectPublicKeyInfo_ChangedPrefixByte_Rejects(int index)
    {
        byte[] spki = Fixtures.BuildP256SubjectPublicKeyInfo();
        spki[index] ^= 0x01;

        Assert.False(P256PublicKey.TryFromSubjectPublicKeyInfo(spki, out _));
    }

    /// <summary>Verifies a well-formed encoding whose point is not on the curve is rejected.</summary>
    [Fact]
    public void TryFromSubjectPublicKeyInfo_PointNotOnCurve_Rejects()
    {
        byte[] spki = Fixtures.BuildP256SubjectPublicKeyInfo();
        spki[^1] ^= 0x01;

        Assert.False(P256PublicKey.TryFromSubjectPublicKeyInfo(spki, out _));
    }

    /// <summary>Verifies the fingerprint, as raw digest bytes or as text, can never stand in for the full key.</summary>
    [Fact]
    public void TryFromSubjectPublicKeyInfo_FingerprintInsteadOfKey_Rejects()
    {
        P256PublicKey key = P256PublicKey.FromSubjectPublicKeyInfo(Fixtures.BuildP256SubjectPublicKeyInfo());

        Assert.False(P256PublicKey.TryFromSubjectPublicKeyInfo(SHA256.HashData(key.SubjectPublicKeyInfo), out _));
        Assert.False(P256PublicKey.TryFromSubjectPublicKeyInfo(Encoding.ASCII.GetBytes(key.Fingerprint), out _));
    }

    /// <summary>Verifies value equality follows the exact SubjectPublicKeyInfo bytes.</summary>
    [Fact]
    public void Equals_SameAndDifferentKeys_ComparesExactBytes()
    {
        P256PublicKey host = P256PublicKey.FromSubjectPublicKeyInfo(Fixtures.BuildP256SubjectPublicKeyInfo());
        P256PublicKey sameHost = P256PublicKey.FromSubjectPublicKeyInfo(Fixtures.BuildP256SubjectPublicKeyInfo());
        P256PublicKey client = P256PublicKey.FromSubjectPublicKeyInfo(Fixtures.BuildP256SubjectPublicKeyInfo(
            "3059301306072a8648ce3d020106082a8648ce3d03010703420004fdf05d25acd08029eabaf4dbafefda88f9df6acc278a88cff9d67934b71e15cbe8be70c2230db3aa58ac0bd5fd14dbb15e6c94a249aee685890665d145a07484"));

        Assert.Equal(host, sameHost);
        Assert.Equal(host.GetHashCode(), sameHost.GetHashCode());
        Assert.NotEqual(host, client);
        Assert.False(host.Equals(null));
    }
}
