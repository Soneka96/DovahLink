using System.Text.Json.Nodes;

namespace DovahLinkValidationClient.Tests;

/// <summary>Exercises RenameOutcomePayload's Decode behavior against every canonical outcome fixture.</summary>
public class RenameOutcomePayloadTests
{
    /// <summary>Verifies that Decode matches the canonical renamed fixture.</summary>
    [Fact]
    public void DecodeMatchesTheCanonicalRenamedFixture()
    {
        RenameOutcomePayload payload =
            RenameOutcomePayload.Decode(ProtocolFixtures.ReadFixturePayload("rename/rename-outcome-renamed.json"));

        Assert.Equal("renamed", payload.Outcome);
        Assert.Equal("New Name", payload.DisplayName);
    }

    /// <summary>Verifies that Decode matches the canonical invalid_display_name fixture.</summary>
    [Fact]
    public void DecodeMatchesTheCanonicalInvalidDisplayNameFixture()
    {
        RenameOutcomePayload payload = RenameOutcomePayload.Decode(
            ProtocolFixtures.ReadFixturePayload("rename/rename-outcome-invalid-display-name.json"));

        Assert.Equal("invalid_display_name", payload.Outcome);
        Assert.Null(payload.DisplayName);
    }

    /// <summary>Verifies that Decode matches the canonical not_trusted fixture.</summary>
    [Fact]
    public void DecodeMatchesTheCanonicalNotTrustedFixture()
    {
        RenameOutcomePayload payload = RenameOutcomePayload.Decode(
            ProtocolFixtures.ReadFixturePayload("rename/rename-outcome-not-trusted.json"));

        Assert.Equal("not_trusted", payload.Outcome);
        Assert.Null(payload.DisplayName);
    }

    /// <summary>Verifies that Decode throws FormatException when outcome is missing.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenOutcomeIsMissing()
    {
        var payload = new JsonObject { ["displayName"] = null };

        Assert.Throws<FormatException>(() => RenameOutcomePayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when displayName's key is absent
    /// entirely, not merely null.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenDisplayNameKeyIsAbsent()
    {
        var payload = new JsonObject { ["outcome"] = "not_trusted" };

        Assert.Throws<FormatException>(() => RenameOutcomePayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when outcome is the wrong JSON type.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenOutcomeIsTheWrongType()
    {
        var payload = new JsonObject { ["outcome"] = 1, ["displayName"] = null };

        Assert.Throws<FormatException>(() => RenameOutcomePayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when displayName is the wrong JSON type.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenDisplayNameIsTheWrongType()
    {
        var payload = new JsonObject { ["outcome"] = "not_trusted", ["displayName"] = 1 };

        Assert.Throws<FormatException>(() => RenameOutcomePayload.Decode(payload));
    }
}
