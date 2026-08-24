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

    /// <summary>Verifies that Decode accepts an in-progress status with a pending credential.</summary>
    [Fact]
    public void DecodeAcceptsInProgressWithNullExpiry()
    {
        PairingStatusPayload payload = PairingStatusPayload.Decode(new JsonObject
        {
            ["state"] = "in_progress",
            ["expiresInSeconds"] = null,
        });

        Assert.Equal("in_progress", payload.State);
        Assert.Null(payload.ExpiresInSeconds);
    }

    /// <summary>Verifies that Decode accepts zero as an available expiry.</summary>
    [Fact]
    public void DecodeAcceptsZeroAvailableExpiry()
    {
        PairingStatusPayload payload = PairingStatusPayload.Decode(new JsonObject
        {
            ["state"] = "available",
            ["expiresInSeconds"] = 0,
        });

        Assert.Equal(0, payload.ExpiresInSeconds);
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

    /// <summary>Verifies that Decode rejects an unknown pairing state.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenStateIsUnknown()
    {
        var payload = new JsonObject
        {
            ["state"] = "unknown",
            ["expiresInSeconds"] = null,
        };

        Assert.Throws<FormatException>(() => PairingStatusPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode rejects a negative expiry.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenExpiresInSecondsIsNegative()
    {
        var payload = new JsonObject
        {
            ["state"] = "available",
            ["expiresInSeconds"] = -1,
        };

        Assert.Throws<FormatException>(() => PairingStatusPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode rejects an available status without an expiry.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenAvailableExpiryIsMissing()
    {
        var payload = new JsonObject { ["state"] = "available" };

        Assert.Throws<FormatException>(() => PairingStatusPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode rejects an available status with a null expiry.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenAvailableExpiryIsNull()
    {
        var payload = new JsonObject
        {
            ["state"] = "available",
            ["expiresInSeconds"] = null,
        };

        Assert.Throws<FormatException>(() => PairingStatusPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode rejects a numeric expiry for an unavailable status.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenUnavailableExpiryIsNumeric()
    {
        var payload = new JsonObject
        {
            ["state"] = "unavailable",
            ["expiresInSeconds"] = 1,
        };

        Assert.Throws<FormatException>(() => PairingStatusPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode rejects an in-progress status without an expiry key.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenInProgressExpiryIsMissing()
    {
        var payload = new JsonObject { ["state"] = "in_progress" };

        Assert.Throws<FormatException>(() => PairingStatusPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode rejects an expiry key for another device's pairing.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenOtherDevicePairingExpiryIsPresent()
    {
        var payload = new JsonObject
        {
            ["state"] = "other_device_pairing",
            ["expiresInSeconds"] = null,
        };

        Assert.Throws<FormatException>(() => PairingStatusPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode rejects an unavailable status without an expiry key.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenUnavailableExpiryIsMissing()
    {
        var payload = new JsonObject { ["state"] = "unavailable" };

        Assert.Throws<FormatException>(() => PairingStatusPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode rejects a negative in-progress expiry.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenInProgressExpiryIsNegative()
    {
        var payload = new JsonObject
        {
            ["state"] = "in_progress",
            ["expiresInSeconds"] = -1,
        };

        Assert.Throws<FormatException>(() => PairingStatusPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode rejects a wrong-type in-progress expiry.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenInProgressExpiryHasTheWrongType()
    {
        var payload = new JsonObject
        {
            ["state"] = "in_progress",
            ["expiresInSeconds"] = "soon",
        };

        Assert.Throws<FormatException>(() => PairingStatusPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode rejects a numeric expiry for another device's pairing.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenOtherDevicePairingExpiryIsNumeric()
    {
        var payload = new JsonObject
        {
            ["state"] = "other_device_pairing",
            ["expiresInSeconds"] = 1,
        };

        Assert.Throws<FormatException>(() => PairingStatusPayload.Decode(payload));
    }
}
