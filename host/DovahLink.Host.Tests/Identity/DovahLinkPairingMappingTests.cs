using DovahLink.Host.Identity;

namespace DovahLink.Host.Tests.Identity;

/// <summary>Tests the DovahLink pairing UUID, application-identity, and authority-scope encodings against the frozen mapping vectors.</summary>
public sealed class DovahLinkPairingMappingTests
{
    /// <summary>Verifies UUIDs encode in RFC 9562 network order, never the mixed-endian .NET layout (vector V04).</summary>
    /// <param name="uuid">The UUID text.</param>
    /// <param name="networkOrderHex">The expected RFC 9562 bytes.</param>
    /// <param name="mixedEndianHex">The .NET <see cref="Guid.ToByteArray()"/> bytes that must not appear.</param>
    [Theory]
    [InlineData("00112233-4455-6677-8899-aabbccddeeff", "00112233445566778899aabbccddeeff", "33221100554477668899aabbccddeeff")]
    [InlineData("0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0", "0f1e2d3c4b5a69788796a5b4c3d2e1f0", "3c2d1e0f5a4b78698796a5b4c3d2e1f0")]
    public void EncodeUuid_NetworkOrderNotGuidToByteArray(string uuid, string networkOrderHex, string mixedEndianHex)
    {
        Guid value = Guid.Parse(uuid);

        byte[] encoded = DovahLinkPairingMapping.EncodeUuid(value);

        Assert.Equal(networkOrderHex, Convert.ToHexStringLower(encoded));
        Assert.Equal(mixedEndianHex, Convert.ToHexStringLower(value.ToByteArray()));
        Assert.NotEqual(value.ToByteArray(), encoded);
    }

    /// <summary>Verifies every accepted text form of one UUID encodes to the same 16 bytes.</summary>
    [Fact]
    public void EncodeUuid_TextFormAliases_EncodeIdentically()
    {
        byte[] canonical = DovahLinkPairingMapping.EncodeUuid(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"));

        Assert.Equal(canonical, DovahLinkPairingMapping.EncodeUuid(Guid.Parse("{00112233-4455-6677-8899-AABBCCDDEEFF}")));
        Assert.Equal(canonical, DovahLinkPairingMapping.EncodeUuid(Guid.Parse("00112233445566778899aabbccddeeff")));
    }

    /// <summary>Verifies the Host and Client application identities of one UUID differ only by the role byte (vector V03).</summary>
    [Fact]
    public void EncodeApplicationIdentity_SameUuidBothRoles_MatchVectorsAndDiffer()
    {
        Guid uuid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

        byte[] host = DovahLinkPairingMapping.EncodeApplicationIdentity(DovahLinkPairingRole.Host, uuid);
        byte[] client = DovahLinkPairingMapping.EncodeApplicationIdentity(DovahLinkPairingRole.Client, uuid);

        Assert.Equal(
            "646f7661686c696e6b2e6170706c69636174696f6e2d6964656e746974792e76310100112233445566778899aabbccddeeff",
            Convert.ToHexStringLower(host));
        Assert.Equal(
            "646f7661686c696e6b2e6170706c69636174696f6e2d6964656e746974792e76310200112233445566778899aabbccddeeff",
            Convert.ToHexStringLower(client));
        Assert.Equal(50, host.Length);
        Assert.NotEqual(host, client);
    }

    /// <summary>Verifies the Host authority scope is exactly the frozen 47 bytes and differs from the client scope (vector V14).</summary>
    [Fact]
    public void EncodeAuthorityScope_MatchesVectorsAndSeparatesRoles()
    {
        var hostId = new HostId(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"));
        Guid clientUuid = Guid.Parse("0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0");

        byte[] host = DovahLinkPairingMapping.EncodeHostAuthorityScope(hostId);
        byte[] client = DovahLinkPairingMapping.EncodeAuthorityScope(DovahLinkPairingRole.Client, clientUuid);
        byte[] sameUuidClient = DovahLinkPairingMapping.EncodeAuthorityScope(DovahLinkPairingRole.Client, hostId.Value);

        Assert.Equal(
            "646f7661686c696e6b2e70616972696e672d617574686f726974792e76310100112233445566778899aabbccddeeff",
            Convert.ToHexStringLower(host));
        Assert.Equal(
            "646f7661686c696e6b2e70616972696e672d617574686f726974792e7631020f1e2d3c4b5a69788796a5b4c3d2e1f0",
            Convert.ToHexStringLower(client));
        Assert.Equal(
            "646f7661686c696e6b2e70616972696e672d617574686f726974792e76310200112233445566778899aabbccddeeff",
            Convert.ToHexStringLower(sameUuidClient));
        Assert.Equal(47, host.Length);
        Assert.NotEqual(host, sameUuidClient);
    }

    /// <summary>Verifies the authority scope uses its own domain, so it never equals an application identity prefix.</summary>
    [Fact]
    public void EncodeAuthorityScope_UsesDomainDistinctFromApplicationIdentity()
    {
        Guid uuid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

        byte[] scope = DovahLinkPairingMapping.EncodeAuthorityScope(DovahLinkPairingRole.Host, uuid);
        byte[] identity = DovahLinkPairingMapping.EncodeApplicationIdentity(DovahLinkPairingRole.Host, uuid);

        Assert.False(identity.AsSpan().EndsWith(scope));
        Assert.True(scope.AsSpan()[^16..].SequenceEqual(identity.AsSpan()[^16..]));
    }

    /// <summary>Verifies the nil UUID never becomes an installation identity or scope.</summary>
    [Fact]
    public void Encoders_NilUuid_Throw()
    {
        Assert.Throws<ArgumentException>(() => DovahLinkPairingMapping.EncodeUuid(Guid.Empty));
        Assert.Throws<ArgumentException>(() => DovahLinkPairingMapping.EncodeApplicationIdentity(DovahLinkPairingRole.Host, Guid.Empty));
        Assert.Throws<ArgumentException>(() => DovahLinkPairingMapping.EncodeAuthorityScope(DovahLinkPairingRole.Client, Guid.Empty));
        Assert.Throws<ArgumentException>(() => DovahLinkPairingMapping.EncodeHostAuthorityScope(default));
    }

    /// <summary>Verifies an undefined role byte is never encoded.</summary>
    /// <param name="role">An undefined role value.</param>
    [Theory]
    [InlineData((DovahLinkPairingRole)0)]
    [InlineData((DovahLinkPairingRole)3)]
    public void Encoders_UndefinedRole_Throw(DovahLinkPairingRole role)
    {
        Guid uuid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

        Assert.Throws<ArgumentOutOfRangeException>(() => DovahLinkPairingMapping.EncodeApplicationIdentity(role, uuid));
        Assert.Throws<ArgumentOutOfRangeException>(() => DovahLinkPairingMapping.EncodeAuthorityScope(role, uuid));
    }
}
