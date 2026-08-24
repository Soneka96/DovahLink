using System.Text.Json.Nodes;

namespace DovahLinkValidationClient.Tests;

/// <summary>Exercises PairingStatusPayload's Decode behavior against canonical fixtures.</summary>
public class PairingStatusPayloadTests
{
    /// <summary>Verifies that Decode matches the canonical available fixture.</summary>
    [Fact]
    public void DecodeMatchesTheCanonicalAvailableFixture()
    {
        PairingStatusPayload payload =
            PairingStatusPayload.Decode(ProtocolFixtures.ReadFixturePayload("pairing/pairing-status-available.json"));

        Assert.Equal("available", payload.State);
        Assert.Equal(300, payload.ExpiresInSeconds);
    }

    /// <summary>Verifies that Decode matches the canonical unavailable fixture, whose
    /// expiresInSeconds is present as null.</summary>
    [Fact]
    public void DecodeMatchesTheCanonicalUnavailableFixture()
    {
        PairingStatusPayload payload = PairingStatusPayload.Decode(
            ProtocolFixtures.ReadFixturePayload("pairing/pairing-status-unavailable.json"));

        Assert.Equal("unavailable", payload.State);
        Assert.Null(payload.ExpiresInSeconds);
    }

    /// <summary>Verifies that Decode matches the canonical in_progress fixture.</summary>
    [Fact]
    public void DecodeMatchesTheCanonicalInProgressFixture()
    {
        PairingStatusPayload payload = PairingStatusPayload.Decode(
            ProtocolFixtures.ReadFixturePayload("pairing/pairing-status-in-progress.json"));

        Assert.Equal("in_progress", payload.State);
        Assert.Equal(187, payload.ExpiresInSeconds);
    }

    /// <summary>Verifies that Decode matches the canonical other_device_pairing fixture, whose
    /// expiresInSeconds is omitted entirely.</summary>
    [Fact]
    public void DecodeMatchesTheCanonicalOtherDevicePairingFixture()
    {
        PairingStatusPayload payload = PairingStatusPayload.Decode(
            ProtocolFixtures.ReadFixturePayload("pairing/pairing-status-other-device.json"));

        Assert.Equal("other_device_pairing", payload.State);
        Assert.Null(payload.ExpiresInSeconds);
    }

    /// <summary>Verifies that Decode throws FormatException when state is missing.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenStateIsMissing()
    {
        var payload = new JsonObject { ["expiresInSeconds"] = 300 };

        Assert.Throws<FormatException>(() => PairingStatusPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when state is the wrong JSON type.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenStateIsTheWrongType()
    {
        var payload = new JsonObject { ["state"] = 1 };

        Assert.Throws<FormatException>(() => PairingStatusPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when expiresInSeconds is the wrong
    /// JSON type.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenExpiresInSecondsIsTheWrongType()
    {
        var payload = new JsonObject
        {
            ["state"] = "available",
            ["expiresInSeconds"] = "not a number",
        };

        Assert.Throws<FormatException>(() => PairingStatusPayload.Decode(payload));
    }
}
