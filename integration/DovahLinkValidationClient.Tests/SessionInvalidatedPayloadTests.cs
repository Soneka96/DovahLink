using System.Text.Json.Nodes;

namespace DovahLinkValidationClient.Tests;

/// <summary>
/// Exercises SessionInvalidatedPayload's Decode behavior. No canonical protocol/fixtures/ file
/// exists for session_invalidated (matching the Bridge's and Dart SDK's own equivalent tests,
/// which decode inline literals for the same reason), so every reason value is built inline here.
/// </summary>
public class SessionInvalidatedPayloadTests
{
    /// <summary>Verifies that Decode decodes the revoked reason.</summary>
    [Fact]
    public void DecodeDecodesTheRevokedReason()
    {
        SessionInvalidatedPayload payload = SessionInvalidatedPayload.Decode(new JsonObject { ["reason"] = "revoked" });

        Assert.Equal("revoked", payload.Reason);
    }

    /// <summary>Verifies that Decode decodes the blocked reason.</summary>
    [Fact]
    public void DecodeDecodesTheBlockedReason()
    {
        SessionInvalidatedPayload payload = SessionInvalidatedPayload.Decode(new JsonObject { ["reason"] = "blocked" });

        Assert.Equal("blocked", payload.Reason);
    }

    /// <summary>Verifies that Decode decodes the trust_reset reason.</summary>
    [Fact]
    public void DecodeDecodesTheTrustResetReason()
    {
        SessionInvalidatedPayload payload =
            SessionInvalidatedPayload.Decode(new JsonObject { ["reason"] = "trust_reset" });

        Assert.Equal("trust_reset", payload.Reason);
    }

    /// <summary>Verifies that Decode decodes the factory_reset reason.</summary>
    [Fact]
    public void DecodeDecodesTheFactoryResetReason()
    {
        SessionInvalidatedPayload payload =
            SessionInvalidatedPayload.Decode(new JsonObject { ["reason"] = "factory_reset" });

        Assert.Equal("factory_reset", payload.Reason);
    }

    /// <summary>Verifies that Decode throws FormatException when reason is missing.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenReasonIsMissing()
    {
        var payload = new JsonObject();

        Assert.Throws<FormatException>(() => SessionInvalidatedPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when reason is the wrong JSON type.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenReasonIsTheWrongType()
    {
        var payload = new JsonObject { ["reason"] = 1 };

        Assert.Throws<FormatException>(() => SessionInvalidatedPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode rejects an unknown invalidation reason.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenReasonIsUnknown()
    {
        var payload = new JsonObject { ["reason"] = "unknown" };

        Assert.Throws<FormatException>(() => SessionInvalidatedPayload.Decode(payload));
    }
}
