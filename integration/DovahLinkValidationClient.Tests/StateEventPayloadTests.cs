using System.Text.Json.Nodes;

namespace DovahLinkValidationClient.Tests;

/// <summary>Exercises StateEventPayload's Decode behavior against canonical fixtures.</summary>
public class StateEventPayloadTests
{
    /// <summary>Verifies that Decode matches the canonical state-event fixture.</summary>
    [Fact]
    public void DecodeMatchesTheCanonicalFixture()
    {
        StateEventPayload payload =
            StateEventPayload.Decode(ProtocolFixtures.ReadFixturePayload("state/state-event.json"));

        Assert.Equal("example_area", payload.StateArea);
        Assert.Equal(1, payload.BaseRevision);
        Assert.Equal(2, payload.Revision);
        Assert.Equal("2026-08-11T12:00:02Z", payload.OccurredAt);
        Assert.True(JsonNode.DeepEquals(payload.Data, new JsonObject { ["value"] = 13 }));
    }

    /// <summary>Verifies that Decode decodes the canonical state-event-duplicate fixture at the
    /// same revision as state-event.</summary>
    [Fact]
    public void DecodeDecodesTheCanonicalDuplicateFixtureAtTheSameRevisionAsStateEvent()
    {
        StateEventPayload payload =
            StateEventPayload.Decode(ProtocolFixtures.ReadFixturePayload("state/state-event-duplicate.json"));

        Assert.Equal(1, payload.BaseRevision);
        Assert.Equal(2, payload.Revision);
    }

    /// <summary>Verifies that Decode decodes the canonical state-event-revision-gap fixture.</summary>
    [Fact]
    public void DecodeDecodesTheCanonicalRevisionGapFixture()
    {
        StateEventPayload payload =
            StateEventPayload.Decode(ProtocolFixtures.ReadFixturePayload("state/state-event-revision-gap.json"));

        Assert.Equal(5, payload.BaseRevision);
        Assert.Equal(6, payload.Revision);
    }

    /// <summary>Verifies that Decode decodes the canonical state-event-stale fixture.</summary>
    [Fact]
    public void DecodeDecodesTheCanonicalStaleFixture()
    {
        StateEventPayload payload =
            StateEventPayload.Decode(ProtocolFixtures.ReadFixturePayload("state/state-event-stale.json"));

        Assert.Equal(0, payload.BaseRevision);
        Assert.Equal(1, payload.Revision);
    }

    /// <summary>Verifies that Decode throws FormatException when stateArea is missing.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenStateAreaIsMissing()
    {
        var payload = new JsonObject
        {
            ["baseRevision"] = 1,
            ["revision"] = 2,
            ["occurredAt"] = "2026-08-11T12:00:00Z",
            ["data"] = new JsonObject(),
        };

        Assert.Throws<FormatException>(() => StateEventPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when baseRevision is missing.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenBaseRevisionIsMissing()
    {
        var payload = new JsonObject
        {
            ["stateArea"] = "example_area",
            ["revision"] = 2,
            ["occurredAt"] = "2026-08-11T12:00:00Z",
            ["data"] = new JsonObject(),
        };

        Assert.Throws<FormatException>(() => StateEventPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when revision is missing.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenRevisionIsMissing()
    {
        var payload = new JsonObject
        {
            ["stateArea"] = "example_area",
            ["baseRevision"] = 1,
            ["occurredAt"] = "2026-08-11T12:00:00Z",
            ["data"] = new JsonObject(),
        };

        Assert.Throws<FormatException>(() => StateEventPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when occurredAt is missing.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenOccurredAtIsMissing()
    {
        var payload = new JsonObject
        {
            ["stateArea"] = "example_area",
            ["baseRevision"] = 1,
            ["revision"] = 2,
            ["data"] = new JsonObject(),
        };

        Assert.Throws<FormatException>(() => StateEventPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when data is missing.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenDataIsMissing()
    {
        var payload = new JsonObject
        {
            ["stateArea"] = "example_area",
            ["baseRevision"] = 1,
            ["revision"] = 2,
            ["occurredAt"] = "2026-08-11T12:00:00Z",
        };

        Assert.Throws<FormatException>(() => StateEventPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when stateArea is the wrong JSON type.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenStateAreaIsTheWrongType()
    {
        var payload = new JsonObject
        {
            ["stateArea"] = 1,
            ["baseRevision"] = 1,
            ["revision"] = 2,
            ["occurredAt"] = "2026-08-11T12:00:00Z",
            ["data"] = new JsonObject(),
        };

        Assert.Throws<FormatException>(() => StateEventPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when baseRevision is the wrong JSON type.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenBaseRevisionIsTheWrongType()
    {
        var payload = new JsonObject
        {
            ["stateArea"] = "example_area",
            ["baseRevision"] = "one",
            ["revision"] = 2,
            ["occurredAt"] = "2026-08-11T12:00:00Z",
            ["data"] = new JsonObject(),
        };

        Assert.Throws<FormatException>(() => StateEventPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when revision is the wrong JSON type.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenRevisionIsTheWrongType()
    {
        var payload = new JsonObject
        {
            ["stateArea"] = "example_area",
            ["baseRevision"] = 1,
            ["revision"] = "two",
            ["occurredAt"] = "2026-08-11T12:00:00Z",
            ["data"] = new JsonObject(),
        };

        Assert.Throws<FormatException>(() => StateEventPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when baseRevision is negative.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenBaseRevisionIsNegative()
    {
        var payload = new JsonObject
        {
            ["stateArea"] = "example_area",
            ["baseRevision"] = -1,
            ["revision"] = 0,
            ["occurredAt"] = "2026-08-11T12:00:00Z",
            ["data"] = new JsonObject(),
        };

        Assert.Throws<FormatException>(() => StateEventPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when revision is negative.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenRevisionIsNegative()
    {
        // Negative baseRevision would itself already fail ValidateNonNegative first, so
        // baseRevision here is 0 to isolate the revision check specifically.
        var payload = new JsonObject
        {
            ["stateArea"] = "example_area",
            ["baseRevision"] = 0,
            ["revision"] = -1,
            ["occurredAt"] = "2026-08-11T12:00:00Z",
            ["data"] = new JsonObject(),
        };

        Assert.Throws<FormatException>(() => StateEventPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode accepts a fractional-seconds UTC timestamp.</summary>
    [Fact]
    public void DecodeAcceptsAFractionalSecondsUtcTimestamp()
    {
        StateEventPayload payload = StateEventPayload.Decode(new JsonObject
        {
            ["stateArea"] = "example_area",
            ["baseRevision"] = 1,
            ["revision"] = 2,
            ["occurredAt"] = "2026-08-11T12:00:00.1Z",
            ["data"] = new JsonObject(),
        });

        Assert.Equal("2026-08-11T12:00:00.1Z", payload.OccurredAt);
    }

    /// <summary>Verifies that Decode throws FormatException when occurredAt is a well-formed but
    /// non-UTC timestamp.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenOccurredAtIsNotUtc()
    {
        var payload = new JsonObject
        {
            ["stateArea"] = "example_area",
            ["baseRevision"] = 1,
            ["revision"] = 2,
            ["occurredAt"] = "2026-08-11T12:00:00+02:00",
            ["data"] = new JsonObject(),
        };

        Assert.Throws<FormatException>(() => StateEventPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode rejects a timestamp without an explicit UTC designator.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenOccurredAtHasNoTimezone()
    {
        var payload = new JsonObject
        {
            ["stateArea"] = "example_area",
            ["baseRevision"] = 1,
            ["revision"] = 2,
            ["occurredAt"] = "2026-08-11T12:00:00",
            ["data"] = new JsonObject(),
        };

        Assert.Throws<FormatException>(() => StateEventPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode rejects a timestamp with a non-RFC 3339 separator.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenOccurredAtUsesASpaceSeparator()
    {
        var payload = new JsonObject
        {
            ["stateArea"] = "example_area",
            ["baseRevision"] = 1,
            ["revision"] = 2,
            ["occurredAt"] = "2026-08-11 12:00:00Z",
            ["data"] = new JsonObject(),
        };

        Assert.Throws<FormatException>(() => StateEventPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode rejects a lowercase UTC designator.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenOccurredAtUsesLowercaseUtcDesignator()
    {
        var payload = new JsonObject
        {
            ["stateArea"] = "example_area",
            ["baseRevision"] = 1,
            ["revision"] = 2,
            ["occurredAt"] = "2026-08-11T12:00:00z",
            ["data"] = new JsonObject(),
        };

        Assert.Throws<FormatException>(() => StateEventPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode rejects an invalid minute.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenOccurredAtHasAnInvalidMinute()
    {
        var payload = new JsonObject
        {
            ["stateArea"] = "example_area",
            ["baseRevision"] = 1,
            ["revision"] = 2,
            ["occurredAt"] = "2026-08-11T12:60:00Z",
            ["data"] = new JsonObject(),
        };

        Assert.Throws<FormatException>(() => StateEventPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode rejects an invalid calendar date.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenOccurredAtHasAnInvalidCalendarDate()
    {
        var payload = new JsonObject
        {
            ["stateArea"] = "example_area",
            ["baseRevision"] = 1,
            ["revision"] = 2,
            ["occurredAt"] = "2026-02-30T12:00:00Z",
            ["data"] = new JsonObject(),
        };

        Assert.Throws<FormatException>(() => StateEventPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when occurredAt is the wrong JSON type.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenOccurredAtIsTheWrongType()
    {
        var payload = new JsonObject
        {
            ["stateArea"] = "example_area",
            ["baseRevision"] = 1,
            ["revision"] = 2,
            ["occurredAt"] = 1,
            ["data"] = new JsonObject(),
        };

        Assert.Throws<FormatException>(() => StateEventPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when occurredAt is malformed.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenOccurredAtIsMalformed()
    {
        var payload = new JsonObject
        {
            ["stateArea"] = "example_area",
            ["baseRevision"] = 1,
            ["revision"] = 2,
            ["occurredAt"] = "not-a-timestamp",
            ["data"] = new JsonObject(),
        };

        Assert.Throws<FormatException>(() => StateEventPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when data is not an object.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenDataIsNotAnObject()
    {
        var payload = new JsonObject
        {
            ["stateArea"] = "example_area",
            ["baseRevision"] = 1,
            ["revision"] = 2,
            ["occurredAt"] = "2026-08-11T12:00:00Z",
            ["data"] = "not an object",
        };

        Assert.Throws<FormatException>(() => StateEventPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when revision does not equal
    /// baseRevision + 1.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenRevisionDoesNotEqualBaseRevisionPlusOne()
    {
        var payload = new JsonObject
        {
            ["stateArea"] = "example_area",
            ["baseRevision"] = 1,
            ["revision"] = 5,
            ["occurredAt"] = "2026-08-11T12:00:00Z",
            ["data"] = new JsonObject(),
        };

        Assert.Throws<FormatException>(() => StateEventPayload.Decode(payload));
    }
}
