using System.Text.Json.Nodes;

namespace DovahLinkValidationClient.Tests;

/// <summary>Exercises ErrorPayload's Decode behavior against canonical fixtures.</summary>
public class ErrorPayloadTests
{
    /// <summary>Verifies that Decode matches the canonical unauthenticated (invalid token) fixture.</summary>
    [Fact]
    public void DecodeMatchesTheCanonicalUnauthenticatedInvalidTokenFixture()
    {
        ErrorPayload payload = ErrorPayload.Decode(
            ProtocolFixtures.ReadFixturePayload("errors/error-unauthenticated-invalid-token.json"));

        Assert.Equal("unauthenticated", payload.Code);
        Assert.Equal("Invalid one-time token", payload.Message);
        Assert.False(payload.Retryable);
        Assert.Null(payload.Details);
    }

    /// <summary>Verifies that Decode matches the canonical rate_limited fixture, whose retryable
    /// is true (the only fixture where it isn't false).</summary>
    [Fact]
    public void DecodeMatchesTheCanonicalRateLimitedFixture()
    {
        ErrorPayload payload = ErrorPayload.Decode(ProtocolFixtures.ReadFixturePayload("errors/error-rate-limited.json"));

        Assert.Equal("rate_limited", payload.Code);
        Assert.True(payload.Retryable);
    }

    /// <summary>Verifies that Decode matches the canonical unsupported_capability fixture.</summary>
    [Fact]
    public void DecodeMatchesTheCanonicalUnsupportedCapabilityFixture()
    {
        ErrorPayload payload =
            ErrorPayload.Decode(ProtocolFixtures.ReadFixturePayload("errors/error-unsupported-capability.json"));

        Assert.Equal("unsupported_capability", payload.Code);
        Assert.False(payload.Retryable);
    }

    /// <summary>Verifies that Decode decodes structured details when present.</summary>
    [Fact]
    public void DecodeDecodesStructuredDetailsWhenPresent()
    {
        var payload = new JsonObject
        {
            ["code"] = "internal_error",
            ["message"] = "Unable to build response",
            ["retryable"] = false,
            ["details"] = new JsonObject { ["hint"] = "retry later" },
        };

        ErrorPayload decoded = ErrorPayload.Decode(payload);

        Assert.NotNull(decoded.Details);
        Assert.True(JsonNode.DeepEquals(decoded.Details, new JsonObject { ["hint"] = "retry later" }));
    }

    /// <summary>Verifies that Decode treats an omitted details key the same as an explicit null.</summary>
    [Fact]
    public void DecodeTreatsAnOmittedDetailsKeyAsNull()
    {
        var payload = new JsonObject
        {
            ["code"] = "internal_error",
            ["message"] = "Unable to build response",
            ["retryable"] = false,
        };

        ErrorPayload decoded = ErrorPayload.Decode(payload);

        Assert.Null(decoded.Details);
    }

    /// <summary>Verifies that Decode treats details present as an explicit JSON null the same as
    /// an omitted key, matching every canonical error fixture's own shape.</summary>
    [Fact]
    public void DecodeTreatsAnExplicitNullDetailsAsNull()
    {
        var payload = new JsonObject
        {
            ["code"] = "internal_error",
            ["message"] = "Unable to build response",
            ["retryable"] = false,
            ["details"] = null,
        };

        ErrorPayload decoded = ErrorPayload.Decode(payload);

        Assert.Null(decoded.Details);
    }

    /// <summary>Verifies that Decode throws FormatException when code is missing.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenCodeIsMissing()
    {
        var payload = new JsonObject
        {
            ["message"] = "Token validation failed",
            ["retryable"] = false,
            ["details"] = null,
        };

        Assert.Throws<FormatException>(() => ErrorPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when message is missing.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenMessageIsMissing()
    {
        var payload = new JsonObject
        {
            ["code"] = "unauthenticated",
            ["retryable"] = false,
            ["details"] = null,
        };

        Assert.Throws<FormatException>(() => ErrorPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when retryable is missing.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenRetryableIsMissing()
    {
        var payload = new JsonObject
        {
            ["code"] = "unauthenticated",
            ["message"] = "Token validation failed",
            ["details"] = null,
        };

        Assert.Throws<FormatException>(() => ErrorPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when code is the wrong JSON type.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenCodeIsTheWrongType()
    {
        var payload = new JsonObject
        {
            ["code"] = 1,
            ["message"] = "Token validation failed",
            ["retryable"] = false,
            ["details"] = null,
        };

        Assert.Throws<FormatException>(() => ErrorPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when message is the wrong JSON type.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenMessageIsTheWrongType()
    {
        var payload = new JsonObject
        {
            ["code"] = "unauthenticated",
            ["message"] = 1,
            ["retryable"] = false,
            ["details"] = null,
        };

        Assert.Throws<FormatException>(() => ErrorPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when retryable is the wrong JSON type.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenRetryableIsTheWrongType()
    {
        var payload = new JsonObject
        {
            ["code"] = "unauthenticated",
            ["message"] = "Token validation failed",
            ["retryable"] = "false",
            ["details"] = null,
        };

        Assert.Throws<FormatException>(() => ErrorPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode rejects an unknown error code.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenCodeIsUnknown()
    {
        var payload = new JsonObject
        {
            ["code"] = "unknown_error",
            ["message"] = "Something failed",
            ["retryable"] = false,
            ["details"] = null,
        };

        Assert.Throws<FormatException>(() => ErrorPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode accepts every registered error code not covered by fixtures.</summary>
    /// <param name="code">The registered error code to decode.</param>
    [Theory]
    [InlineData("malformed_message")]
    [InlineData("frame_too_large")]
    [InlineData("unauthorized")]
    [InlineData("revoked")]
    [InlineData("blocked")]
    [InlineData("replayed_message")]
    [InlineData("stale_session")]
    [InlineData("internal_error")]
    public void DecodeAcceptsEveryUncoveredRegisteredCode(string code)
    {
        var payload = new JsonObject
        {
            ["code"] = code,
            ["message"] = "Something failed",
            ["retryable"] = false,
            ["details"] = null,
        };

        ErrorPayload decoded = ErrorPayload.Decode(payload);

        Assert.Equal(code, decoded.Code);
    }
}
