using System.Security.Cryptography;
using System.Text;
using DovahLink.Host.Identity;

namespace DovahLink.Host.Tests.Identity;

/// <summary>
/// Tests the DovahLink Bootstrap fields and their canonical frame against the frozen mapping vectors,
/// including the exact-frame comparison the Host uses to turn candidate client values into evidence.
/// </summary>
public sealed class DovahLinkBootstrapTests
{
    /// <summary>The canonical frame of the vector Host Bootstrap (V01).</summary>
    private const string HostFrameHex =
        "5341535041495200012000000032646f7661686c696e6b2e6170706c69636174696f6e2d6964656e746974792e76310100112233445566778899aabbccddeeff00000020646f7661686c696e6b2e65636473612d703235362e73706b692d6465722e76310000005b3059301306072a8648ce3d020106082a8648ce3d03010703420004f2422a662eb6e5065e3ea5587ed92dd959deff9b9e4115bb76dcb02abf07144a68e354b81cc01714608a8ecd61f8d9ac453cda8b0d20056623db09859432498a0000002a646f7661686c696e6b2e7361732d70616972696e672e626f6f7473747261702d76312e70616972696e67";

    /// <summary>The canonical frame of the vector Client Bootstrap (V02), also the authenticated peer frame of V12.</summary>
    private const string ClientFrameHex =
        "5341535041495200012000000032646f7661686c696e6b2e6170706c69636174696f6e2d6964656e746974792e7631020f1e2d3c4b5a69788796a5b4c3d2e1f000000020646f7661686c696e6b2e65636473612d703235362e73706b692d6465722e76310000005b3059301306072a8648ce3d020106082a8648ce3d03010703420004fdf05d25acd08029eabaf4dbafefda88f9df6acc278a88cff9d67934b71e15cbe8be70c2230db3aa58ac0bd5fd14dbb15e6c94a249aee685890665d145a074840000002a646f7661686c696e6b2e7361732d70616972696e672e626f6f7473747261702d76312e70616972696e67";

    /// <summary>The published Client test key.</summary>
    private const string ClientKeyHex =
        "3059301306072a8648ce3d020106082a8648ce3d03010703420004fdf05d25acd08029eabaf4dbafefda88f9df6acc278a88cff9d67934b71e15cbe8be70c2230db3aa58ac0bd5fd14dbb15e6c94a249aee685890665d145a07484";

    /// <summary>The published substitute test key (vector V09).</summary>
    private const string SubstituteKeyHex =
        "3059301306072a8648ce3d020106082a8648ce3d030107034200040bb2280b1872c24858a96435ba7cc34a9ec35f479bb1927225a82a9ae9a15fd90d083305f94466576611f5a3033370d93bbb3caaf7f73aec48c81b8898ddec2f";

    /// <summary>The vector Host installation UUID.</summary>
    private static readonly Guid HostUuid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

    /// <summary>The vector client installation UUID.</summary>
    private static readonly Guid ClientUuid = Guid.Parse("0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0");

    /// <summary>Verifies the Host Bootstrap fields and canonical frame reproduce vector V01 exactly.</summary>
    [Fact]
    public void ForHost_VectorInputs_ReproduceV01()
    {
        DovahLinkBootstrap bootstrap = DovahLinkBootstrap.ForHost(new HostId(HostUuid), HostKey());

        byte[] frame = bootstrap.EncodeCanonicalFrame();

        Assert.Equal(HostFrameHex, Convert.ToHexStringLower(frame));
        Assert.Equal(241, frame.Length);
        Assert.Equal("232a56f2392a3dd83ee3847663af9b13ca4e4deeb9ea018746b4c681a0f2d4af", Convert.ToHexStringLower(SHA256.HashData(frame)));
        Assert.Equal(
            "646f7661686c696e6b2e6170706c69636174696f6e2d6964656e746974792e76310100112233445566778899aabbccddeeff",
            Convert.ToHexStringLower(bootstrap.ApplicationIdentity));
        Assert.Equal("dovahlink.ecdsa-p256.spki-der.v1", Encoding.ASCII.GetString(bootstrap.KeyAlgorithm));
        Assert.True(bootstrap.PublicKey.SequenceEqual(Fixtures.BuildP256SubjectPublicKeyInfo()));
        Assert.Equal("dovahlink.sas-pairing.bootstrap-v1.pairing", Encoding.ASCII.GetString(bootstrap.SharedContext));
    }

    /// <summary>
    /// Verifies the expected client Bootstrap built from the candidate client ID and key reproduces
    /// vector V02, which equals the authenticated peer frame of the Host-side E-13 check (V12).
    /// </summary>
    [Fact]
    public void ForClientCandidate_VectorInputs_ReproduceV02AndMatchAuthenticatedFrameV12()
    {
        byte[] expected = ClientCandidateFrame(ClientUuid, ClientKeyHex);

        Assert.Equal(ClientFrameHex, Convert.ToHexStringLower(expected));
        Assert.Equal("2391656c6384a54177d9442c4cd57667208ab6c045f2c916901e88094331fa6c", Convert.ToHexStringLower(SHA256.HashData(expected)));
        Assert.True(expected.AsSpan().SequenceEqual(Convert.FromHexString(ClientFrameHex)));
    }

    /// <summary>Verifies the publicKey field is the full SPKI, so a fingerprint can never take its place.</summary>
    [Fact]
    public void ForHost_PublicKeyIsFullSpkiNotFingerprint()
    {
        P256PublicKey key = HostKey();

        DovahLinkBootstrap bootstrap = DovahLinkBootstrap.ForHost(new HostId(HostUuid), key);

        Assert.Equal(91, bootstrap.PublicKey.Length);
        Assert.False(bootstrap.PublicKey.SequenceEqual(Encoding.ASCII.GetBytes(key.Fingerprint)));
        Assert.False(bootstrap.PublicKey.SequenceEqual(SHA256.HashData(key.SubjectPublicKeyInfo)));
    }

    /// <summary>Verifies both sides' independently compiled shared contexts are byte-equal (vector V06).</summary>
    [Fact]
    public void SharedContext_HostAndClient_AreTheSameConstant()
    {
        DovahLinkBootstrap host = DovahLinkBootstrap.ForHost(new HostId(HostUuid), HostKey());
        DovahLinkBootstrap client = DovahLinkBootstrap.ForClientCandidate(new ClientId(ClientUuid), ClientKey());

        Assert.Equal(
            "646f7661686c696e6b2e7361732d70616972696e672e626f6f7473747261702d76312e70616972696e67",
            Convert.ToHexStringLower(host.SharedContext));
        Assert.True(host.SharedContext.SequenceEqual(client.SharedContext));
    }

    /// <summary>Verifies two different keys give different frames (vector V05).</summary>
    [Fact]
    public void EncodeCanonicalFrame_DifferentKeys_Differ()
    {
        byte[] host = DovahLinkBootstrap.ForHost(new HostId(HostUuid), HostKey()).EncodeCanonicalFrame();
        byte[] sameIdOtherKey = DovahLinkBootstrap.ForHost(new HostId(HostUuid), ClientKey()).EncodeCanonicalFrame();

        Assert.NotEqual(host, sameIdOtherKey);
    }

    /// <summary>
    /// Verifies the exact-frame comparison rejects each one-field candidate change against the
    /// authenticated client frame: a changed key (V09), a one-bit UUID change (V10), and the client
    /// UUID under the Host role byte (V10b).
    /// </summary>
    [Fact]
    public void ExactFrameComparison_ChangedCandidateField_DoesNotMatch()
    {
        byte[] authenticated = Convert.FromHexString(ClientFrameHex);

        byte[] substituteKey = ClientCandidateFrame(ClientUuid, SubstituteKeyHex);
        byte[] oneBitUuid = ClientCandidateFrame(Guid.Parse("0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f1"), ClientKeyHex);
        byte[] hostRole = DovahLinkBootstrap.ForHost(new HostId(ClientUuid), ClientKey()).EncodeCanonicalFrame();

        Assert.False(authenticated.AsSpan().SequenceEqual(substituteKey));
        Assert.False(authenticated.AsSpan().SequenceEqual(oneBitUuid));
        Assert.False(authenticated.AsSpan().SequenceEqual(hostRole));
        Assert.Equal(
            "5341535041495200012000000032646f7661686c696e6b2e6170706c69636174696f6e2d6964656e746974792e7631010f1e2d3c4b5a69788796a5b4c3d2e1f000000020646f7661686c696e6b2e65636473612d703235362e73706b692d6465722e76310000005b3059301306072a8648ce3d020106082a8648ce3d03010703420004fdf05d25acd08029eabaf4dbafefda88f9df6acc278a88cff9d67934b71e15cbe8be70c2230db3aa58ac0bd5fd14dbb15e6c94a249aee685890665d145a074840000002a646f7661686c696e6b2e7361732d70616972696e672e626f6f7473747261702d76312e70616972696e67",
            Convert.ToHexStringLower(hostRole));
    }

    /// <summary>
    /// Verifies a peer frame that differs from the expected frame in one byte of any field (algorithm,
    /// key, or context, as in vectors V07 and V08), or that carries trailing bytes, is not equal.
    /// </summary>
    /// <param name="offset">The frame offset to change; -1 appends one trailing byte instead.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(14)]
    [InlineData(63)]
    [InlineData(68)]
    [InlineData(100)]
    [InlineData(195)]
    [InlineData(240)]
    [InlineData(-1)]
    public void ExactFrameComparison_PeerFrameChangedOrExtended_DoesNotMatch(int offset)
    {
        byte[] expected = ClientCandidateFrame(ClientUuid, ClientKeyHex);
        byte[] peer = Convert.FromHexString(ClientFrameHex);
        if (offset < 0)
        {
            peer = [.. peer, 0x00];
        }
        else
        {
            peer[offset] ^= 0x01;
        }

        Assert.False(expected.AsSpan().SequenceEqual(peer));
    }

    /// <summary>
    /// Verifies equality of the client ID alone authenticates nothing: a frame with the candidate's
    /// application identity but another key is a different frame.
    /// </summary>
    [Fact]
    public void ExactFrameComparison_SameClientIdOtherKey_DoesNotMatch()
    {
        DovahLinkBootstrap expected = DovahLinkBootstrap.ForClientCandidate(new ClientId(ClientUuid), ClientKey());
        DovahLinkBootstrap sameIdOtherKey = DovahLinkBootstrap.ForClientCandidate(
            new ClientId(ClientUuid), P256PublicKey.FromSubjectPublicKeyInfo(Convert.FromHexString(SubstituteKeyHex)));

        Assert.True(expected.ApplicationIdentity.SequenceEqual(sameIdOtherKey.ApplicationIdentity));
        Assert.False(expected.EncodeCanonicalFrame().AsSpan().SequenceEqual(sameIdOtherKey.EncodeCanonicalFrame()));
    }

    /// <summary>Verifies a shared context with an appended per-attempt contribution is not the frozen constant (vector V11).</summary>
    [Fact]
    public void ExactFrameComparison_ContextWithAttemptBytes_DoesNotMatch()
    {
        byte[] expected = ClientCandidateFrame(ClientUuid, ClientKeyHex);
        byte[] withAttemptBytes = Convert.FromHexString(
            "5341535041495200012000000032646f7661686c696e6b2e6170706c69636174696f6e2d6964656e746974792e7631020f1e2d3c4b5a69788796a5b4c3d2e1f000000020646f7661686c696e6b2e65636473612d703235362e73706b692d6465722e76310000005b3059301306072a8648ce3d020106082a8648ce3d03010703420004fdf05d25acd08029eabaf4dbafefda88f9df6acc278a88cff9d67934b71e15cbe8be70c2230db3aa58ac0bd5fd14dbb15e6c94a249aee685890665d145a074840000003a646f7661686c696e6b2e7361732d70616972696e672e626f6f7473747261702d76312e70616972696e67000102030405060708090a0b0c0d0e0f");

        Assert.False(expected.AsSpan().SequenceEqual(withAttemptBytes));
    }

    /// <summary>Verifies every field has its fixed DovahLink length and every domain constant is plain ASCII.</summary>
    [Fact]
    public void Fields_HaveFixedLengthsAndAsciiConstants()
    {
        DovahLinkBootstrap host = DovahLinkBootstrap.ForHost(new HostId(HostUuid), HostKey());
        DovahLinkBootstrap client = DovahLinkBootstrap.ForClientCandidate(new ClientId(ClientUuid), ClientKey());

        foreach (DovahLinkBootstrap bootstrap in new[] { host, client })
        {
            Assert.Equal(50, bootstrap.ApplicationIdentity.Length);
            Assert.Equal(32, bootstrap.KeyAlgorithm.Length);
            Assert.Equal(91, bootstrap.PublicKey.Length);
            Assert.Equal(42, bootstrap.SharedContext.Length);
        }

        foreach (string constant in new[]
        {
            Constants.PairingApplicationIdentityDomain, Constants.PairingKeyAlgorithm,
            Constants.PairingSharedContext, Constants.PairingAuthorityScopeDomain,
        })
        {
            Assert.True(Ascii.IsValid(constant), constant);
        }
    }

    /// <summary>Verifies each encoded frame is deterministic and a fresh copy, so a caller changing one cannot alter the Bootstrap.</summary>
    [Fact]
    public void EncodeCanonicalFrame_ReturnsIndependentCopies()
    {
        DovahLinkBootstrap bootstrap = DovahLinkBootstrap.ForHost(new HostId(HostUuid), HostKey());
        byte[] first = bootstrap.EncodeCanonicalFrame();
        Assert.Equal(first, bootstrap.EncodeCanonicalFrame());
        Assert.NotSame(first, bootstrap.EncodeCanonicalFrame());

        first[^1] ^= 0xff;

        Assert.Equal(HostFrameHex, Convert.ToHexStringLower(bootstrap.EncodeCanonicalFrame()));
    }

    /// <summary>Verifies an empty identity or a missing key never builds a Bootstrap.</summary>
    [Fact]
    public void Factories_InvalidInputs_Throw()
    {
        Assert.Throws<ArgumentException>(() => DovahLinkBootstrap.ForHost(default, HostKey()));
        Assert.Throws<ArgumentException>(() => DovahLinkBootstrap.ForClientCandidate(new ClientId(Guid.Empty), ClientKey()));
        Assert.Throws<ArgumentNullException>(() => DovahLinkBootstrap.ForHost(new HostId(HostUuid), null!));
        Assert.Throws<ArgumentNullException>(() => DovahLinkBootstrap.ForClientCandidate(new ClientId(ClientUuid), null!));
    }

    /// <summary>Builds the published Host test key.</summary>
    /// <returns>The Host test key.</returns>
    private static P256PublicKey HostKey() => P256PublicKey.FromSubjectPublicKeyInfo(Fixtures.BuildP256SubjectPublicKeyInfo());

    /// <summary>Builds the published Client test key.</summary>
    /// <returns>The Client test key.</returns>
    private static P256PublicKey ClientKey() => P256PublicKey.FromSubjectPublicKeyInfo(Convert.FromHexString(ClientKeyHex));

    /// <summary>Encodes the expected client frame from candidate values.</summary>
    /// <param name="clientUuid">The candidate client UUID.</param>
    /// <param name="keyHex">The candidate client key SPKI hex.</param>
    /// <returns>The expected canonical frame.</returns>
    private static byte[] ClientCandidateFrame(Guid clientUuid, string keyHex) =>
        DovahLinkBootstrap.ForClientCandidate(
            new ClientId(clientUuid), P256PublicKey.FromSubjectPublicKeyInfo(Convert.FromHexString(keyHex))).EncodeCanonicalFrame();
}
