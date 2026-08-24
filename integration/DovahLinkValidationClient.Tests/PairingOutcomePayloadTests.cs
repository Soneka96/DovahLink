using System.Text.Json.Nodes;

namespace DovahLinkValidationClient.Tests;

/// <summary>Exercises PairingOutcomePayload's Decode behavior against every canonical outcome fixture.</summary>
public class PairingOutcomePayloadTests
{
    /// <summary>Verifies that Decode matches the canonical credential_issued fixture.</summary>
    [Fact]
    public void DecodeMatchesTheCanonicalCredentialIssuedFixture()
    {
        PairingOutcomePayload payload = PairingOutcomePayload.Decode(
            ProtocolFixtures.ReadFixturePayload("pairing/pairing-outcome-credential-issued.json"));

        Assert.Equal("credential_issued", payload.Outcome);
        Assert.Equal("a1b2c3d4e5f6", payload.Credential);
        Assert.Null(payload.ShortId);
        Assert.Equal("My PC", payload.DisplayName);
        Assert.Null(payload.RetryAfterSeconds);
    }

    /// <summary>Verifies that Decode matches the canonical trusted fixture.</summary>
    [Fact]
    public void DecodeMatchesTheCanonicalTrustedFixture()
    {
        PairingOutcomePayload payload =
            PairingOutcomePayload.Decode(ProtocolFixtures.ReadFixturePayload("pairing/pairing-outcome-trusted.json"));

        Assert.Equal("trusted", payload.Outcome);
        Assert.Equal("a1b2c3d4e5f6", payload.Credential);
        Assert.Equal("12345", payload.ShortId);
        Assert.Equal("My PC", payload.DisplayName);
        Assert.Null(payload.RetryAfterSeconds);
    }

    /// <summary>Verifies that Decode matches the canonical already_trusted fixture.</summary>
    [Fact]
    public void DecodeMatchesTheCanonicalAlreadyTrustedFixture()
    {
        PairingOutcomePayload payload = PairingOutcomePayload.Decode(
            ProtocolFixtures.ReadFixturePayload("pairing/pairing-outcome-already-trusted.json"));

        Assert.Equal("already_trusted", payload.Outcome);
        Assert.Equal("a1b2c3d4e5f6", payload.Credential);
        Assert.Equal("12345", payload.ShortId);
        Assert.Equal("My PC", payload.DisplayName);
        Assert.Null(payload.RetryAfterSeconds);
    }

    /// <summary>Verifies that Decode matches the canonical expired fixture, with every optional
    /// field null.</summary>
    [Fact]
    public void DecodeMatchesTheCanonicalExpiredFixture()
    {
        PairingOutcomePayload payload =
            PairingOutcomePayload.Decode(ProtocolFixtures.ReadFixturePayload("pairing/pairing-outcome-expired.json"));

        Assert.Equal("expired", payload.Outcome);
        Assert.Null(payload.Credential);
        Assert.Null(payload.ShortId);
        Assert.Null(payload.DisplayName);
        Assert.Null(payload.RetryAfterSeconds);
    }

    /// <summary>Verifies that Decode matches the canonical invalid fixture.</summary>
    [Fact]
    public void DecodeMatchesTheCanonicalInvalidFixture()
    {
        PairingOutcomePayload payload =
            PairingOutcomePayload.Decode(ProtocolFixtures.ReadFixturePayload("pairing/pairing-outcome-invalid.json"));

        Assert.Equal("invalid", payload.Outcome);
        Assert.Null(payload.Credential);
        Assert.Null(payload.ShortId);
        Assert.Null(payload.DisplayName);
        Assert.Null(payload.RetryAfterSeconds);
    }

    /// <summary>Verifies that Decode matches the canonical pacing_limited fixture, which carries a
    /// real retryAfterSeconds.</summary>
    [Fact]
    public void DecodeMatchesTheCanonicalPacingLimitedFixture()
    {
        PairingOutcomePayload payload = PairingOutcomePayload.Decode(
            ProtocolFixtures.ReadFixturePayload("pairing/pairing-outcome-pacing-limited.json"));

        Assert.Equal("pacing_limited", payload.Outcome);
        Assert.Null(payload.Credential);
        Assert.Null(payload.ShortId);
        Assert.Null(payload.DisplayName);
        Assert.Equal(1, payload.RetryAfterSeconds);
    }

    /// <summary>Verifies that Decode matches the canonical hard_limit_reached fixture.</summary>
    [Fact]
    public void DecodeMatchesTheCanonicalHardLimitReachedFixture()
    {
        PairingOutcomePayload payload = PairingOutcomePayload.Decode(
            ProtocolFixtures.ReadFixturePayload("pairing/pairing-outcome-hard-limit-reached.json"));

        Assert.Equal("hard_limit_reached", payload.Outcome);
        Assert.Null(payload.Credential);
        Assert.Null(payload.ShortId);
        Assert.Null(payload.DisplayName);
        Assert.Null(payload.RetryAfterSeconds);
    }

    /// <summary>Verifies that Decode matches the canonical pending_not_found fixture.</summary>
    [Fact]
    public void DecodeMatchesTheCanonicalPendingNotFoundFixture()
    {
        PairingOutcomePayload payload = PairingOutcomePayload.Decode(
            ProtocolFixtures.ReadFixturePayload("pairing/pairing-outcome-pending-not-found.json"));

        Assert.Equal("pending_not_found", payload.Outcome);
        Assert.Null(payload.Credential);
        Assert.Null(payload.ShortId);
        Assert.Null(payload.DisplayName);
        Assert.Null(payload.RetryAfterSeconds);
    }

    /// <summary>Verifies that Decode matches the canonical renotified fixture.</summary>
    [Fact]
    public void DecodeMatchesTheCanonicalRenotifiedFixture()
    {
        PairingOutcomePayload payload = PairingOutcomePayload.Decode(
            ProtocolFixtures.ReadFixturePayload("pairing/pairing-outcome-renotified.json"));

        Assert.Equal("renotified", payload.Outcome);
        Assert.Null(payload.Credential);
        Assert.Null(payload.ShortId);
        Assert.Null(payload.DisplayName);
        Assert.Null(payload.RetryAfterSeconds);
    }

    /// <summary>Verifies that Decode matches the canonical renotify_cooldown fixture, which carries
    /// a real retryAfterSeconds.</summary>
    [Fact]
    public void DecodeMatchesTheCanonicalRenotifyCooldownFixture()
    {
        PairingOutcomePayload payload = PairingOutcomePayload.Decode(
            ProtocolFixtures.ReadFixturePayload("pairing/pairing-outcome-renotify-cooldown.json"));

        Assert.Equal("renotify_cooldown", payload.Outcome);
        Assert.Null(payload.Credential);
        Assert.Null(payload.ShortId);
        Assert.Null(payload.DisplayName);
        Assert.Equal(3, payload.RetryAfterSeconds);
    }

    /// <summary>Verifies that Decode matches the canonical cancelled fixture.</summary>
    [Fact]
    public void DecodeMatchesTheCanonicalCancelledFixture()
    {
        PairingOutcomePayload payload = PairingOutcomePayload.Decode(
            ProtocolFixtures.ReadFixturePayload("pairing/pairing-outcome-cancelled.json"));

        Assert.Equal("cancelled", payload.Outcome);
        Assert.Null(payload.Credential);
        Assert.Null(payload.ShortId);
        Assert.Null(payload.DisplayName);
        Assert.Null(payload.RetryAfterSeconds);
    }

    /// <summary>Verifies that Decode matches the canonical already_idle fixture.</summary>
    [Fact]
    public void DecodeMatchesTheCanonicalAlreadyIdleFixture()
    {
        PairingOutcomePayload payload = PairingOutcomePayload.Decode(
            ProtocolFixtures.ReadFixturePayload("pairing/pairing-outcome-already-idle.json"));

        Assert.Equal("already_idle", payload.Outcome);
        Assert.Null(payload.Credential);
        Assert.Null(payload.ShortId);
        Assert.Null(payload.DisplayName);
        Assert.Null(payload.RetryAfterSeconds);
    }

    /// <summary>Verifies that Decode throws FormatException when outcome is missing.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenOutcomeIsMissing()
    {
        var payload = new JsonObject
        {
            ["credential"] = null,
            ["shortId"] = null,
            ["displayName"] = null,
            ["retryAfterSeconds"] = null,
        };

        Assert.Throws<FormatException>(() => PairingOutcomePayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when credential's key is absent
    /// entirely, not merely null.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenCredentialKeyIsAbsent()
    {
        var payload = new JsonObject
        {
            ["outcome"] = "expired",
            ["shortId"] = null,
            ["displayName"] = null,
            ["retryAfterSeconds"] = null,
        };

        Assert.Throws<FormatException>(() => PairingOutcomePayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when shortId's key is absent entirely.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenShortIdKeyIsAbsent()
    {
        var payload = new JsonObject
        {
            ["outcome"] = "expired",
            ["credential"] = null,
            ["displayName"] = null,
            ["retryAfterSeconds"] = null,
        };

        Assert.Throws<FormatException>(() => PairingOutcomePayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when displayName's key is absent entirely.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenDisplayNameKeyIsAbsent()
    {
        var payload = new JsonObject
        {
            ["outcome"] = "expired",
            ["credential"] = null,
            ["shortId"] = null,
            ["retryAfterSeconds"] = null,
        };

        Assert.Throws<FormatException>(() => PairingOutcomePayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when retryAfterSeconds' key is absent
    /// entirely.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenRetryAfterSecondsKeyIsAbsent()
    {
        var payload = new JsonObject
        {
            ["outcome"] = "expired",
            ["credential"] = null,
            ["shortId"] = null,
            ["displayName"] = null,
        };

        Assert.Throws<FormatException>(() => PairingOutcomePayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when retryAfterSeconds is the wrong
    /// JSON type.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenRetryAfterSecondsIsTheWrongType()
    {
        var payload = new JsonObject
        {
            ["outcome"] = "pacing_limited",
            ["credential"] = null,
            ["shortId"] = null,
            ["displayName"] = null,
            ["retryAfterSeconds"] = "soon",
        };

        Assert.Throws<FormatException>(() => PairingOutcomePayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when outcome is the wrong JSON type.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenOutcomeIsTheWrongType()
    {
        var payload = new JsonObject
        {
            ["outcome"] = 1,
            ["credential"] = null,
            ["shortId"] = null,
            ["displayName"] = null,
            ["retryAfterSeconds"] = null,
        };

        Assert.Throws<FormatException>(() => PairingOutcomePayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when credential is the wrong JSON type.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenCredentialIsTheWrongType()
    {
        var payload = new JsonObject
        {
            ["outcome"] = "expired",
            ["credential"] = 1,
            ["shortId"] = null,
            ["displayName"] = null,
            ["retryAfterSeconds"] = null,
        };

        Assert.Throws<FormatException>(() => PairingOutcomePayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when shortId is the wrong JSON type.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenShortIdIsTheWrongType()
    {
        var payload = new JsonObject
        {
            ["outcome"] = "expired",
            ["credential"] = null,
            ["shortId"] = 1,
            ["displayName"] = null,
            ["retryAfterSeconds"] = null,
        };

        Assert.Throws<FormatException>(() => PairingOutcomePayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when displayName is the wrong JSON type.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenDisplayNameIsTheWrongType()
    {
        var payload = new JsonObject
        {
            ["outcome"] = "expired",
            ["credential"] = null,
            ["shortId"] = null,
            ["displayName"] = 1,
            ["retryAfterSeconds"] = null,
        };

        Assert.Throws<FormatException>(() => PairingOutcomePayload.Decode(payload));
    }

    /// <summary>Verifies that Decode rejects an unknown outcome value.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenOutcomeIsUnknown()
    {
        var payload = new JsonObject
        {
            ["outcome"] = "unknown",
            ["credential"] = null,
            ["shortId"] = null,
            ["displayName"] = null,
            ["retryAfterSeconds"] = null,
        };

        Assert.Throws<FormatException>(() => PairingOutcomePayload.Decode(payload));
    }

    /// <summary>Verifies that Decode rejects a credential on an outcome that does not issue one.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenCredentialIsPresentForExpiredOutcome()
    {
        var payload = new JsonObject
        {
            ["outcome"] = "expired",
            ["credential"] = "a1b2c3d4e5f6",
            ["shortId"] = null,
            ["displayName"] = null,
            ["retryAfterSeconds"] = null,
        };

        Assert.Throws<FormatException>(() => PairingOutcomePayload.Decode(payload));
    }

    /// <summary>Verifies that Decode rejects an empty credential.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenCredentialIsEmpty()
    {
        var payload = new JsonObject
        {
            ["outcome"] = "trusted",
            ["credential"] = "",
            ["shortId"] = "12345",
            ["displayName"] = null,
            ["retryAfterSeconds"] = null,
        };

        Assert.Throws<FormatException>(() => PairingOutcomePayload.Decode(payload));
    }

    /// <summary>Verifies that Decode rejects a short ID on an outcome that does not issue one.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenShortIdIsPresentForExpiredOutcome()
    {
        var payload = new JsonObject
        {
            ["outcome"] = "expired",
            ["credential"] = null,
            ["shortId"] = "12345",
            ["displayName"] = null,
            ["retryAfterSeconds"] = null,
        };

        Assert.Throws<FormatException>(() => PairingOutcomePayload.Decode(payload));
    }

    /// <summary>Verifies that Decode rejects an empty short ID.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenShortIdIsEmpty()
    {
        var payload = new JsonObject
        {
            ["outcome"] = "trusted",
            ["credential"] = "a1b2c3d4e5f6",
            ["shortId"] = "",
            ["displayName"] = null,
            ["retryAfterSeconds"] = null,
        };

        Assert.Throws<FormatException>(() => PairingOutcomePayload.Decode(payload));
    }

    /// <summary>Verifies that Decode rejects a display name on an outcome without credentials.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenDisplayNameIsPresentForExpiredOutcome()
    {
        var payload = new JsonObject
        {
            ["outcome"] = "expired",
            ["credential"] = null,
            ["shortId"] = null,
            ["displayName"] = "My PC",
            ["retryAfterSeconds"] = null,
        };

        Assert.Throws<FormatException>(() => PairingOutcomePayload.Decode(payload));
    }

    /// <summary>Verifies that Decode requires a retry value for pacing-limited outcomes.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenPacingRetryValueIsNull()
    {
        var payload = new JsonObject
        {
            ["outcome"] = "pacing_limited",
            ["credential"] = null,
            ["shortId"] = null,
            ["displayName"] = null,
            ["retryAfterSeconds"] = null,
        };

        Assert.Throws<FormatException>(() => PairingOutcomePayload.Decode(payload));
    }

    /// <summary>Verifies that Decode rejects a retry value on an outcome that does not retry.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenRetryValueIsPresentForExpiredOutcome()
    {
        var payload = new JsonObject
        {
            ["outcome"] = "expired",
            ["credential"] = null,
            ["shortId"] = null,
            ["displayName"] = null,
            ["retryAfterSeconds"] = 1,
        };

        Assert.Throws<FormatException>(() => PairingOutcomePayload.Decode(payload));
    }

    /// <summary>Verifies that Decode rejects a negative retry value.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenRetryValueIsNegative()
    {
        var payload = new JsonObject
        {
            ["outcome"] = "pacing_limited",
            ["credential"] = null,
            ["shortId"] = null,
            ["displayName"] = null,
            ["retryAfterSeconds"] = -1,
        };

        Assert.Throws<FormatException>(() => PairingOutcomePayload.Decode(payload));
    }

    /// <summary>Verifies that Decode accepts a credential-bearing outcome without a display name.</summary>
    [Fact]
    public void DecodeAcceptsCredentialIssuedOutcomeWithNullDisplayName()
    {
        PairingOutcomePayload payload = PairingOutcomePayload.Decode(new JsonObject
        {
            ["outcome"] = "credential_issued",
            ["credential"] = "a1b2c3d4e5f6",
            ["shortId"] = null,
            ["displayName"] = null,
            ["retryAfterSeconds"] = null,
        });

        Assert.Null(payload.DisplayName);
    }

    /// <summary>Verifies that Decode accepts a zero pacing retry value.</summary>
    [Fact]
    public void DecodeAcceptsZeroPacingRetryValue()
    {
        PairingOutcomePayload payload = PairingOutcomePayload.Decode(new JsonObject
        {
            ["outcome"] = "pacing_limited",
            ["credential"] = null,
            ["shortId"] = null,
            ["displayName"] = null,
            ["retryAfterSeconds"] = 0,
        });

        Assert.Equal(0, payload.RetryAfterSeconds);
    }

    /// <summary>Verifies that Decode requires a retry value for renotify cooldown.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenRenotifyCooldownRetryValueIsNull()
    {
        var payload = new JsonObject
        {
            ["outcome"] = "renotify_cooldown",
            ["credential"] = null,
            ["shortId"] = null,
            ["displayName"] = null,
            ["retryAfterSeconds"] = null,
        };

        Assert.Throws<FormatException>(() => PairingOutcomePayload.Decode(payload));
    }

    /// <summary>Verifies that Decode rejects a short ID on credential_issued.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenCredentialIssuedHasShortId()
    {
        var payload = new JsonObject
        {
            ["outcome"] = "credential_issued",
            ["credential"] = "a1b2c3d4e5f6",
            ["shortId"] = "12345",
            ["displayName"] = null,
            ["retryAfterSeconds"] = null,
        };

        Assert.Throws<FormatException>(() => PairingOutcomePayload.Decode(payload));
    }

    /// <summary>Verifies that Decode requires a credential for trusted.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenTrustedCredentialIsNull()
    {
        var payload = new JsonObject
        {
            ["outcome"] = "trusted",
            ["credential"] = null,
            ["shortId"] = "12345",
            ["displayName"] = null,
            ["retryAfterSeconds"] = null,
        };

        Assert.Throws<FormatException>(() => PairingOutcomePayload.Decode(payload));
    }

    /// <summary>Verifies that Decode requires a short ID for trusted.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenTrustedShortIdIsNull()
    {
        var payload = new JsonObject
        {
            ["outcome"] = "trusted",
            ["credential"] = "a1b2c3d4e5f6",
            ["shortId"] = null,
            ["displayName"] = null,
            ["retryAfterSeconds"] = null,
        };

        Assert.Throws<FormatException>(() => PairingOutcomePayload.Decode(payload));
    }
}
