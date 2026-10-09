using System.Reflection;
using DovahLink.Host.Identity;
using DovahLink.Host.PairingCeremony;

namespace DovahLink.Host.Tests.PairingCeremony;

/// <summary>
/// Tests that a local result becomes evidence only when the peer is the Initiator, the profile is the
/// frozen one, the context is this Host's own, and the whole authenticated peer frame equals the frame
/// built from candidate values (E-13).
/// </summary>
public sealed class CeremonyResultValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly CeremonyResultValidator validator = new();

    /// <summary>Verifies the honest result becomes detached evidence carrying the ceremony identity and exact peer frame.</summary>
    [Fact]
    public void Validate_HonestResult_ProducesEvidence()
    {
        byte[] expected = Fixtures.BuildClientCandidateFrame();

        CeremonyEvidenceResult outcome = validator.Validate(Fixtures.BuildCeremonyResultSnapshot(), Fixtures.BuildCeremonyBootstrapFields(), expected);

        Assert.Null(outcome.Rejection);
        SasCeremonyCompletedLocally evidence = outcome.Evidence!;
        Assert.True(evidence.CeremonyIdentity.SequenceEqual(Enumerable.Repeat((byte)0x5a, 32).ToArray()));
        Assert.True(evidence.AuthenticatedPeerBootstrap.SequenceEqual(expected));
    }

    /// <summary>Verifies the evidence is its own copy: later changes to the caller's expected frame cannot alter it.</summary>
    [Fact]
    public void Validate_Evidence_IsDetachedFromCallerBuffers()
    {
        byte[] expected = Fixtures.BuildClientCandidateFrame();
        SasCeremonyCompletedLocally evidence = validator.Validate(
            Fixtures.BuildCeremonyResultSnapshot(), Fixtures.BuildCeremonyBootstrapFields(), expected).Evidence!;

        expected[^1] ^= 0xff;

        Assert.True(evidence.AuthenticatedPeerBootstrap.SequenceEqual(Fixtures.BuildClientCandidateFrame()));
    }

    /// <summary>Verifies a Responder peer is rejected, because this Host is always the Responder.</summary>
    [Fact]
    public void Validate_ResponderPeer_IsRejected()
    {
        CeremonyEvidenceResult outcome = Validate(Fixtures.BuildCeremonyResultSnapshot(peerRole: CeremonyPeerRole.Responder));

        Assert.Equal(CeremonyEvidenceRejection.UnexpectedPeerRole, outcome.Rejection);
        Assert.Null(outcome.Evidence);
    }

    /// <summary>Verifies another profile version or identifier is rejected.</summary>
    /// <param name="version">The profile version.</param>
    /// <param name="identifier">The profile identifier text.</param>
    [Theory]
    [InlineData(2u, "sas-pairing-vodozemac-profile-draft-01")]
    [InlineData(0u, "sas-pairing-vodozemac-profile-draft-01")]
    [InlineData(1u, "sas-pairing-vodozemac-profile-draft-02")]
    [InlineData(1u, "sas-pairing-vodozemac-profile-draft-01 ")]
    [InlineData(1u, "")]
    public void Validate_OtherProfile_IsRejected(uint version, string identifier)
    {
        CeremonyEvidenceResult outcome = Validate(Fixtures.BuildCeremonyResultSnapshot(
            profileVersion: version, profileIdentifier: System.Text.Encoding.ASCII.GetBytes(identifier)));

        Assert.Equal(CeremonyEvidenceRejection.UnexpectedProfile, outcome.Rejection);
    }

    /// <summary>Verifies an authenticated context that differs from this Host's own, by one byte or by extra attempt bytes, is rejected.</summary>
    [Fact]
    public void Validate_OtherSharedContext_IsRejected()
    {
        byte[] oneByte = "dovahlink.sas-pairing.bootstrap-v1.pairing"u8.ToArray();
        oneByte[^1] ^= 0x01;
        byte[] withAttemptBytes = [.. "dovahlink.sas-pairing.bootstrap-v1.pairing"u8, .. new byte[16]];

        foreach (byte[] context in new[] { oneByte, withAttemptBytes, Array.Empty<byte>() })
        {
            Assert.Equal(CeremonyEvidenceRejection.SharedContextMismatch, Validate(Fixtures.BuildCeremonyResultSnapshot(sharedContext: context)).Rejection);
        }
    }

    /// <summary>
    /// Verifies the context is checked against this Host's own Bootstrap, never against the value the
    /// peer authenticated: a peer frame and context that agree with each other still fail when this Host's
    /// context differs.
    /// </summary>
    [Fact]
    public void Validate_ContextComparedWithLocalNotPeerValue()
    {
        CeremonyBootstrapFields host = Fixtures.BuildCeremonyBootstrapFields();
        var otherLocal = new CeremonyBootstrapFields(host.ApplicationIdentity, host.KeyAlgorithm, host.PublicKey, "dovahlink.other-context"u8);

        CeremonyEvidenceResult outcome = validator.Validate(Fixtures.BuildCeremonyResultSnapshot(), otherLocal, Fixtures.BuildClientCandidateFrame());

        Assert.Equal(CeremonyEvidenceRejection.SharedContextMismatch, outcome.Rejection);
    }

    /// <summary>
    /// Verifies E-13: an expected frame built from candidates that differ in exactly one field (client ID,
    /// role, key algorithm, public key, or shared context) does not match the authenticated frame.
    /// </summary>
    [Fact]
    public void Validate_CandidateFrameWithOneChangedField_IsRejected()
    {
        const string ClientUuid = "0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0";
        P256PublicKey clientKey = P256PublicKey.FromSubjectPublicKeyInfo(Fixtures.BuildP256SubjectPublicKeyInfo(
            "3059301306072a8648ce3d020106082a8648ce3d03010703420004fdf05d25acd08029eabaf4dbafefda88f9df6acc278a88cff9d67934b71e15cbe8be70c2230db3aa58ac0bd5fd14dbb15e6c94a249aee685890665d145a07484"));
        // The last key-algorithm byte: 10 header bytes, 4 + 50 application-identity bytes, 4 length bytes, then 32 algorithm bytes.
        byte[] keyAlgorithmChanged = Fixtures.BuildClientCandidateFrame();
        const int LastAlgorithmByte = 10 + 4 + 50 + 4 + 31;
        Assert.Equal((byte)'1', keyAlgorithmChanged[LastAlgorithmByte]);
        keyAlgorithmChanged[LastAlgorithmByte] ^= 0x01;
        byte[] contextChanged = Fixtures.BuildClientCandidateFrame();
        contextChanged[^1] ^= 0x01;
        var candidates = new Dictionary<string, byte[]>
        {
            ["clientId"] = Fixtures.BuildClientCandidateFrame(clientUuid: "0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f1"),
            ["role"] = DovahLinkBootstrap.ForHost(new HostId(Guid.Parse(ClientUuid)), clientKey).EncodeCanonicalFrame(),
            ["keyAlgorithm"] = keyAlgorithmChanged,
            ["publicKey"] = Fixtures.BuildClientCandidateFrame(clientKeyHex:
                "3059301306072a8648ce3d020106082a8648ce3d030107034200040bb2280b1872c24858a96435ba7cc34a9ec35f479bb1927225a82a9ae9a15fd90d083305f94466576611f5a3033370d93bbb3caaf7f73aec48c81b8898ddec2f"),
            ["sharedContext"] = contextChanged,
        };

        foreach ((string field, byte[] frame) in candidates)
        {
            Assert.False(frame.AsSpan().SequenceEqual(Fixtures.BuildClientCandidateFrame()), field);
            CeremonyEvidenceResult outcome = validator.Validate(Fixtures.BuildCeremonyResultSnapshot(), Fixtures.BuildCeremonyBootstrapFields(), frame);
            Assert.True(outcome.Rejection == CeremonyEvidenceRejection.PeerBootstrapMismatch, field);
            Assert.Null(outcome.Evidence);
        }
    }

    /// <summary>Verifies a peer frame that only extends, truncates, or is compared with an empty expected frame is rejected.</summary>
    [Fact]
    public void Validate_ExtendedTruncatedOrEmptyFrames_AreRejected()
    {
        byte[] expected = Fixtures.BuildClientCandidateFrame();

        Assert.Equal(CeremonyEvidenceRejection.PeerBootstrapMismatch,
            Validate(Fixtures.BuildCeremonyResultSnapshot(peerBootstrap: [.. expected, 0x00]), expected).Rejection);
        Assert.Equal(CeremonyEvidenceRejection.PeerBootstrapMismatch,
            Validate(Fixtures.BuildCeremonyResultSnapshot(peerBootstrap: expected[..^1]), expected).Rejection);
        Assert.Equal(CeremonyEvidenceRejection.PeerBootstrapMismatch,
            Validate(Fixtures.BuildCeremonyResultSnapshot(peerBootstrap: []), []).Rejection);
    }

    /// <summary>Verifies an equal client ID alone authenticates nothing: the same client ID with another key is rejected.</summary>
    [Fact]
    public void Validate_SameClientIdOtherKey_IsRejected()
    {
        byte[] sameIdOtherKey = Fixtures.BuildClientCandidateFrame(clientKeyHex:
            "3059301306072a8648ce3d020106082a8648ce3d030107034200040bb2280b1872c24858a96435ba7cc34a9ec35f479bb1927225a82a9ae9a15fd90d083305f94466576611f5a3033370d93bbb3caaf7f73aec48c81b8898ddec2f");

        CeremonyEvidenceResult outcome = Validate(Fixtures.BuildCeremonyResultSnapshot(peerBootstrap: sameIdOtherKey));

        Assert.True(sameIdOtherKey.AsSpan(0, 64).SequenceEqual(Fixtures.BuildClientCandidateFrame().AsSpan(0, 64)));
        Assert.Equal(CeremonyEvidenceRejection.PeerBootstrapMismatch, outcome.Rejection);
    }

    /// <summary>
    /// Verifies the detached result and evidence types hold no sas-pairing package type and the evidence
    /// cannot be created outside the validator.
    /// </summary>
    [Fact]
    public void EvidenceTypes_HoldNoPackageTypesAndCannotBeForged()
    {
        foreach (Type type in new[] { typeof(CeremonyResultSnapshot), typeof(SasCeremonyCompletedLocally), typeof(CeremonyEvidenceResult) })
        {
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                Assert.NotEqual("SasPairing", field.FieldType.Namespace);
                Assert.False(typeof(IDisposable).IsAssignableFrom(field.FieldType), $"{type.Name}.{field.Name}");
            }
        }

        Assert.Empty(typeof(SasCeremonyCompletedLocally).GetConstructors());
        Assert.Empty(typeof(CeremonyEvidenceResult).GetConstructors());
    }

    /// <summary>Verifies null arguments are rejected.</summary>
    [Fact]
    public void Validate_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => validator.Validate(null!, Fixtures.BuildCeremonyBootstrapFields(), []));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(Fixtures.BuildCeremonyResultSnapshot(), null!, []));
    }

    /// <summary>Validates a result against this Host's Bootstrap and an expected frame.</summary>
    /// <param name="result">The result.</param>
    /// <param name="expected">The expected peer frame, or <see langword="null"/> for the vector client frame.</param>
    /// <returns>The outcome.</returns>
    private CeremonyEvidenceResult Validate(CeremonyResultSnapshot result, byte[]? expected = null) =>
        validator.Validate(result, Fixtures.BuildCeremonyBootstrapFields(), expected ?? Fixtures.BuildClientCandidateFrame());
}
