using DovahLink.Host.Trust;

namespace DovahLink.Host.Tests.Trust;

/// <summary>Tests for <see cref="CredentialHasher"/>.</summary>
public class CredentialHasherTests
{
    /// <summary>Verifies that hashing the same input twice produces the same hash.</summary>
    [Fact]
    public void Hash_SameInput_ProducesSameHash()
    {
        Assert.Equal(CredentialHasher.Hash("a-credential"), CredentialHasher.Hash("a-credential"));
    }

    /// <summary>Verifies that hashing two different inputs produces different hashes.</summary>
    [Fact]
    public void Hash_DifferentInput_ProducesDifferentHash()
    {
        Assert.NotEqual(CredentialHasher.Hash("first-credential"), CredentialHasher.Hash("second-credential"));
    }

    /// <summary>Verifies that the hash is a 64-character lowercase hex string, matching a known SHA-256 vector for the empty string.</summary>
    [Fact]
    public void Hash_ReturnsLowercaseHexSha256()
    {
        string hash = CredentialHasher.Hash(string.Empty);

        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, hash.ToLowerInvariant());
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", hash);
    }

    /// <summary>Verifies that comparing a value against itself succeeds.</summary>
    [Fact]
    public void FixedTimeEquals_SameValue_ReturnsTrue()
    {
        Assert.True(CredentialHasher.FixedTimeEquals("matching-value", "matching-value"));
    }

    /// <summary>Verifies that comparing two different same-length values fails.</summary>
    [Fact]
    public void FixedTimeEquals_DifferentValueSameLength_ReturnsFalse()
    {
        Assert.False(CredentialHasher.FixedTimeEquals("aaaaaaaa", "bbbbbbbb"));
    }

    /// <summary>Verifies that comparing values of different lengths fails rather than throwing.</summary>
    [Fact]
    public void FixedTimeEquals_DifferentLength_ReturnsFalse()
    {
        Assert.False(CredentialHasher.FixedTimeEquals("short", "a-much-longer-value"));
    }

    /// <summary>Verifies that two empty values compare equal.</summary>
    [Fact]
    public void FixedTimeEquals_BothEmpty_ReturnsTrue()
    {
        Assert.True(CredentialHasher.FixedTimeEquals(string.Empty, string.Empty));
    }

    /// <summary>Verifies that an empty value never matches a non-empty one.</summary>
    [Fact]
    public void FixedTimeEquals_EmptyAgainstNonEmpty_ReturnsFalse()
    {
        Assert.False(CredentialHasher.FixedTimeEquals(string.Empty, "a"));
    }

    /// <summary>
    /// Verifies the actual hash-then-verify workflow <see cref="DovahLink.Host.Pairing.PairingCoordinator"/> relies
    /// on: hashing the same presented credential twice and comparing the results with
    /// <see cref="CredentialHasher.FixedTimeEquals"/> succeeds, while hashing a different credential
    /// does not.
    /// </summary>
    [Fact]
    public void HashThenFixedTimeEquals_SameCredential_VerifiesAndDifferentCredentialDoesNot()
    {
        string storedVerifier = CredentialHasher.Hash("the-real-credential");

        Assert.True(CredentialHasher.FixedTimeEquals(storedVerifier, CredentialHasher.Hash("the-real-credential")));
        Assert.False(CredentialHasher.FixedTimeEquals(storedVerifier, CredentialHasher.Hash("a-wrong-credential")));
    }
}
