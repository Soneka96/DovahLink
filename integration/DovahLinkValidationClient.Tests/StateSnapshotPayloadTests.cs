using System.Text.Json.Nodes;

namespace DovahLinkValidationClient.Tests;

/// <summary>Exercises StateSnapshotPayload's Decode behavior against canonical fixtures.</summary>
public class StateSnapshotPayloadTests
{
    /// <summary>Verifies that Decode matches the canonical state-snapshot fixture.</summary>
    [Fact]
    public void DecodeMatchesTheCanonicalFixture()
    {
        StateSnapshotPayload payload =
            StateSnapshotPayload.Decode(ProtocolFixtures.ReadFixturePayload("state/state-snapshot.json"));

        Assert.Equal("example_area", payload.StateArea);
        Assert.Equal(1L, payload.Revision);
        Assert.Equal("2026-08-11T12:00:00Z", payload.OccurredAt);
        Assert.True(JsonNode.DeepEquals(payload.Data, new JsonObject { ["value"] = 12 }));
    }

    /// <summary>Verifies that Decode matches the canonical state-snapshot-unavailable fixture.</summary>
    [Fact]
    public void DecodeMatchesTheCanonicalUnavailableFixture()
    {
        StateSnapshotPayload payload = StateSnapshotPayload.Decode(
            ProtocolFixtures.ReadFixturePayload("state/state-snapshot-unavailable.json"));

        Assert.True(JsonNode.DeepEquals(payload.Data, new JsonObject { ["value"] = null }));
    }

    /// <summary>Verifies that Decode accepts revision zero as a valid baseline.</summary>
    [Fact]
    public void DecodeAcceptsRevisionZeroAsAValidBaseline()
    {
        StateSnapshotPayload payload = StateSnapshotPayload.Decode(new JsonObject
        {
            ["stateArea"] = "example_area",
            ["revision"] = 0L,
            ["occurredAt"] = "2026-08-11T12:00:00Z",
            ["data"] = new JsonObject(),
        });

        Assert.Equal(0L, payload.Revision);
    }

    /// <summary>Verifies that Decode accepts a fractional-seconds UTC timestamp.</summary>
    [Fact]
    public void DecodeAcceptsAFractionalSecondsUtcTimestamp()
    {
        StateSnapshotPayload payload = StateSnapshotPayload.Decode(new JsonObject
        {
            ["stateArea"] = "example_area",
            ["revision"] = 1L,
            ["occurredAt"] = "2026-08-11T12:00:00.1Z",
            ["data"] = new JsonObject(),
        });

        Assert.Equal("2026-08-11T12:00:00.1Z", payload.OccurredAt);
    }

    /// <summary>Verifies that Decode throws FormatException when stateArea is missing.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenStateAreaIsMissing()
    {
        var payload = new JsonObject
        {
            ["revision"] = 1L,
            ["occurredAt"] = "2026-08-11T12:00:00Z",
            ["data"] = new JsonObject(),
        };

        Assert.Throws<FormatException>(() => StateSnapshotPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when revision is missing.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenRevisionIsMissing()
    {
        var payload = new JsonObject
        {
            ["stateArea"] = "example_area",
            ["occurredAt"] = "2026-08-11T12:00:00Z",
            ["data"] = new JsonObject(),
        };

        Assert.Throws<FormatException>(() => StateSnapshotPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when occurredAt is missing.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenOccurredAtIsMissing()
    {
        var payload = new JsonObject
        {
            ["stateArea"] = "example_area",
            ["revision"] = 1L,
            ["data"] = new JsonObject(),
        };

        Assert.Throws<FormatException>(() => StateSnapshotPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when data is missing.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenDataIsMissing()
    {
        var payload = new JsonObject
        {
            ["stateArea"] = "example_area",
            ["revision"] = 1L,
            ["occurredAt"] = "2026-08-11T12:00:00Z",
        };

        Assert.Throws<FormatException>(() => StateSnapshotPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when stateArea is the wrong JSON type.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenStateAreaIsTheWrongType()
    {
        var payload = new JsonObject
        {
            ["stateArea"] = 1,
            ["revision"] = 1L,
            ["occurredAt"] = "2026-08-11T12:00:00Z",
            ["data"] = new JsonObject(),
        };

        Assert.Throws<FormatException>(() => StateSnapshotPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode rejects an empty state-area identifier.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenStateAreaIsEmpty()
    {
        var payload = new JsonObject
        {
            ["stateArea"] = "",
            ["revision"] = 1L,
            ["occurredAt"] = "2026-08-11T12:00:00Z",
            ["data"] = new JsonObject(),
        };

        Assert.Throws<FormatException>(() => StateSnapshotPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when revision is the wrong JSON type.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenRevisionIsTheWrongType()
    {
        var payload = new JsonObject
        {
            ["stateArea"] = "example_area",
            ["revision"] = "one",
            ["occurredAt"] = "2026-08-11T12:00:00Z",
            ["data"] = new JsonObject(),
        };

        Assert.Throws<FormatException>(() => StateSnapshotPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when revision is negative.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenRevisionIsNegative()
    {
        var payload = new JsonObject
        {
            ["stateArea"] = "example_area",
            ["revision"] = -1L,
            ["occurredAt"] = "2026-08-11T12:00:00Z",
            ["data"] = new JsonObject(),
        };

        Assert.Throws<FormatException>(() => StateSnapshotPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode rejects the smallest signed 64-bit revision.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenRevisionIsLongMinValue()
    {
        var payload = new JsonObject
        {
            ["stateArea"] = "example_area",
            ["revision"] = long.MinValue,
            ["occurredAt"] = "2026-08-11T12:00:00Z",
            ["data"] = new JsonObject(),
        };

        Assert.Throws<FormatException>(() => StateSnapshotPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode rejects a fractional revision.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenRevisionIsFractional()
    {
        var payload = new JsonObject
        {
            ["stateArea"] = "example_area",
            ["revision"] = 1.5,
            ["occurredAt"] = "2026-08-11T12:00:00Z",
            ["data"] = new JsonObject(),
        };

        Assert.Throws<FormatException>(() => StateSnapshotPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode accepts the maximum signed 64-bit revision.</summary>
    [Fact]
    public void DecodeAcceptsMaximumLongRevision()
    {
        StateSnapshotPayload payload = StateSnapshotPayload.Decode(new JsonObject
        {
            ["stateArea"] = "example_area",
            ["revision"] = long.MaxValue,
            ["occurredAt"] = "2026-08-11T12:00:00Z",
            ["data"] = new JsonObject(),
        });

        Assert.Equal(long.MaxValue, payload.Revision);
    }

    /// <summary>Verifies that Decode throws FormatException when occurredAt is the wrong JSON type.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenOccurredAtIsTheWrongType()
    {
        var payload = new JsonObject
        {
            ["stateArea"] = "example_area",
            ["revision"] = 1L,
            ["occurredAt"] = 1,
            ["data"] = new JsonObject(),
        };

        Assert.Throws<FormatException>(() => StateSnapshotPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when occurredAt is malformed.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenOccurredAtIsMalformed()
    {
        var payload = new JsonObject
        {
            ["stateArea"] = "example_area",
            ["revision"] = 1L,
            ["occurredAt"] = "not-a-timestamp",
            ["data"] = new JsonObject(),
        };

        Assert.Throws<FormatException>(() => StateSnapshotPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when occurredAt is a well-formed but
    /// non-UTC timestamp.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenOccurredAtIsNotUtc()
    {
        var payload = new JsonObject
        {
            ["stateArea"] = "example_area",
            ["revision"] = 1L,
            ["occurredAt"] = "2026-08-11T12:00:00+02:00",
            ["data"] = new JsonObject(),
        };

        Assert.Throws<FormatException>(() => StateSnapshotPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode rejects a timestamp without an explicit UTC designator.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenOccurredAtHasNoTimezone()
    {
        var payload = new JsonObject
        {
            ["stateArea"] = "example_area",
            ["revision"] = 1L,
            ["occurredAt"] = "2026-08-11T12:00:00",
            ["data"] = new JsonObject(),
        };

        Assert.Throws<FormatException>(() => StateSnapshotPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode rejects a timestamp with a non-RFC 3339 separator.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenOccurredAtUsesASpaceSeparator()
    {
        var payload = new JsonObject
        {
            ["stateArea"] = "example_area",
            ["revision"] = 1L,
            ["occurredAt"] = "2026-08-11 12:00:00Z",
            ["data"] = new JsonObject(),
        };

        Assert.Throws<FormatException>(() => StateSnapshotPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode rejects a timestamp with a zero numeric offset.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenOccurredAtUsesNumericUtcOffset()
    {
        var payload = new JsonObject
        {
            ["stateArea"] = "example_area",
            ["revision"] = 1L,
            ["occurredAt"] = "2026-08-11T12:00:00+00:00",
            ["data"] = new JsonObject(),
        };

        Assert.Throws<FormatException>(() => StateSnapshotPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode accepts the maximum supported fractional precision.</summary>
    [Fact]
    public void DecodeAcceptsSixFractionalSecondsDigits()
    {
        StateSnapshotPayload payload = StateSnapshotPayload.Decode(new JsonObject
        {
            ["stateArea"] = "example_area",
            ["revision"] = 1L,
            ["occurredAt"] = "2026-08-11T12:00:00.123456Z",
            ["data"] = new JsonObject(),
        });

        Assert.Equal("2026-08-11T12:00:00.123456Z", payload.OccurredAt);
    }

    /// <summary>Verifies that Decode rejects fractional precision beyond the supported maximum.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenOccurredAtHasSevenFractionalSecondsDigits()
    {
        var payload = new JsonObject
        {
            ["stateArea"] = "example_area",
            ["revision"] = 1L,
            ["occurredAt"] = "2026-08-11T12:00:00.1234567Z",
            ["data"] = new JsonObject(),
        };

        Assert.Throws<FormatException>(() => StateSnapshotPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode rejects an invalid hour.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenOccurredAtHasAnInvalidHour()
    {
        var payload = new JsonObject
        {
            ["stateArea"] = "example_area",
            ["revision"] = 1L,
            ["occurredAt"] = "2026-08-11T24:00:00Z",
            ["data"] = new JsonObject(),
        };

        Assert.Throws<FormatException>(() => StateSnapshotPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when data is not an object.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenDataIsNotAnObject()
    {
        var payload = new JsonObject
        {
            ["stateArea"] = "example_area",
            ["revision"] = 1L,
            ["occurredAt"] = "2026-08-11T12:00:00Z",
            ["data"] = "not an object",
        };

        Assert.Throws<FormatException>(() => StateSnapshotPayload.Decode(payload));
    }
}
