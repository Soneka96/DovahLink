using DovahLink.Host.Adapter.Ipc;

namespace DovahLink.Host.Tests.Adapter.Ipc;

/// <summary>Tests for <see cref="AdapterPeerProofVerifier"/>.</summary>
public class AdapterPeerProofVerifierTests
{
    /// <summary>Verifies that a freshly constructed verifier issues a token of the configured bounded length.</summary>
    [Fact]
    public void ExpectedToken_HasConfiguredLength()
    {
        var verifier = new AdapterPeerProofVerifier();

        Assert.Equal(Constants.MaxIpcPeerProofTokenBytes, verifier.ExpectedToken.Length);
    }

    /// <summary>Verifies that two independently constructed verifiers do not generate the same token.</summary>
    [Fact]
    public void ExpectedToken_DiffersAcrossInstances()
    {
        var first = new AdapterPeerProofVerifier();
        var second = new AdapterPeerProofVerifier();

        Assert.False(first.ExpectedToken.SequenceEqual(second.ExpectedToken));
    }

    /// <summary>Verifies that each read of ExpectedToken returns an independent array, not a shared cached copy.</summary>
    [Fact]
    public void ExpectedToken_EachReadReturnsIndependentArray()
    {
        var verifier = new AdapterPeerProofVerifier();

        byte[] firstRead = verifier.ExpectedToken;
        byte[] secondRead = verifier.ExpectedToken;
        firstRead[0] ^= 0xFF;

        Assert.Equal(secondRead, verifier.ExpectedToken);
    }

    /// <summary>Verifies that the exact expected token matches.</summary>
    [Fact]
    public void Matches_ExactToken_ReturnsTrue()
    {
        var verifier = new AdapterPeerProofVerifier();

        Assert.True(verifier.Matches(verifier.ExpectedToken));
    }

    /// <summary>Verifies that a token differing in a single byte does not match.</summary>
    [Fact]
    public void Matches_SingleByteDifference_ReturnsFalse()
    {
        var verifier = new AdapterPeerProofVerifier();
        byte[] presented = verifier.ExpectedToken;
        presented[^1] ^= 0xFF;

        Assert.False(verifier.Matches(presented));
    }

    /// <summary>Verifies that a wholly unrelated token, such as another verifier's, does not match.</summary>
    [Fact]
    public void Matches_UnrelatedToken_ReturnsFalse()
    {
        var verifier = new AdapterPeerProofVerifier();
        var otherVerifier = new AdapterPeerProofVerifier();

        Assert.False(verifier.Matches(otherVerifier.ExpectedToken));
    }

    /// <summary>Verifies that an empty presented token does not match.</summary>
    [Fact]
    public void Matches_EmptyToken_ReturnsFalse()
    {
        var verifier = new AdapterPeerProofVerifier();

        Assert.False(verifier.Matches(ReadOnlySpan<byte>.Empty));
    }

    /// <summary>Verifies that a shorter presented token does not match, and does not throw.</summary>
    [Fact]
    public void Matches_ShorterToken_ReturnsFalse()
    {
        var verifier = new AdapterPeerProofVerifier();

        Assert.False(verifier.Matches(verifier.ExpectedToken.AsSpan(0, Constants.MaxIpcPeerProofTokenBytes - 1)));
    }

    /// <summary>Verifies that a longer presented token does not match, and does not throw.</summary>
    [Fact]
    public void Matches_LongerToken_ReturnsFalse()
    {
        var verifier = new AdapterPeerProofVerifier();
        byte[] longerToken = [.. verifier.ExpectedToken, 0];

        Assert.False(verifier.Matches(longerToken));
    }
}
