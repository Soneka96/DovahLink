using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using DovahLink.Host.Adapter;
using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;
using DovahLink.Host.State;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.Adapter.Ipc;

/// <summary>Tests Skyrim calendar validation, normalization, and complete-only Host application.</summary>
public class GameTimeCaptureHandlerTests
{
    /// <summary>The independently synchronized public area.</summary>
    private static readonly StateAreaId GameTimeArea = new(Constants.GameTimeStateArea);

    /// <summary>Records one normalized value forwarded to shared Host authority.</summary>
    /// <param name="Value">The complete calendar or unavailable marker.</param>
    /// <param name="AreaId">The destination state area.</param>
    /// <param name="Mode">The canonical update mode.</param>
    /// <param name="IsBaseline">Whether this is a resynchronization baseline.</param>
    /// <param name="Source">The Adapter instance and connection generation.</param>
    /// <param name="AdapterSnapshot">The validated Adapter availability snapshot.</param>
    /// <param name="PlayContextId">The captured loaded play context.</param>
    /// <param name="PlayContextGeneration">The captured play-context generation.</param>
    /// <param name="OccurredAt">The Host capture timestamp.</param>
    private sealed record ApplyCall(
        object? Value,
        StateAreaId AreaId,
        UpdateMode Mode,
        bool IsBaseline,
        AdapterCaptureSource Source,
        AdapterAvailabilitySnapshot AdapterSnapshot,
        PlayContextId PlayContextId,
        long PlayContextGeneration,
        DateTimeOffset OccurredAt);

    /// <summary>A strict recorder for calls to shared Host authority.</summary>
    private sealed class RecordingLiveStateApplication : ILiveStateApplication
    {
        /// <summary>Every normalized value sent to the shared application service.</summary>
        public List<ApplyCall> ApplyCalls { get; } = [];

        /// <inheritdoc/>
        public void Apply<TState>(
            UpdateMode mode,
            StateAreaId areaId,
            TState value,
            bool isResynchronizationBaseline,
            AdapterCaptureSource source,
            AdapterAvailabilitySnapshot adapterSnapshot,
            PlayContextId capturedPlayContextId,
            long capturedPlayContextGeneration,
            DateTimeOffset occurredAt) =>
            ApplyCalls.Add(new ApplyCall(
                value,
                areaId,
                mode,
                isResynchronizationBaseline,
                source,
                adapterSnapshot,
                capturedPlayContextId,
                capturedPlayContextGeneration,
                occurredAt));
    }

    /// <summary>The handler and collaborators used by one mapping test.</summary>
    /// <param name="Handler">The game-time handler.</param>
    /// <param name="Application">The shared application recorder.</param>
    private sealed record Fixture(
        GameTimeCaptureHandler Handler,
        RecordingLiveStateApplication Application);

    /// <summary>Builds a handler with strict collaborators.</summary>
    /// <returns>The handler and its test observers.</returns>
    private static Fixture CreateReady()
    {
        var application = new RecordingLiveStateApplication();
        return new Fixture(new GameTimeCaptureHandler(application), application);
    }

    /// <summary>Writes one raw little-endian calendar float.</summary>
    /// <param name="payload">The destination buffer.</param>
    /// <param name="offset">The offset to write at.</param>
    /// <param name="value">The raw global value.</param>
    private static void WriteFloat(byte[] payload, int offset, float value) =>
        BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(offset, sizeof(float)), value);

    /// <summary>Encodes the private raw-global payload followed by a localized month name.</summary>
    /// <param name="year">The raw year global.</param>
    /// <param name="month">The raw zero-based month global.</param>
    /// <param name="day">The raw day global.</param>
    /// <param name="hour">The raw fractional hour global.</param>
    /// <param name="monthName">The localized month display name.</param>
    /// <returns>The private capture payload.</returns>
    private static byte[] EncodeGameTime(float year, float month, float day, float hour, string monthName)
    {
        byte[] nameBytes = Encoding.UTF8.GetBytes(monthName);
        var payload = new byte[17 + nameBytes.Length];
        WriteFloat(payload, 0, year);
        WriteFloat(payload, 4, month);
        WriteFloat(payload, 8, day);
        WriteFloat(payload, 12, hour);
        payload[16] = checked((byte)nameBytes.Length);
        nameBytes.AsSpan().CopyTo(payload.AsSpan(17));
        return payload;
    }

    /// <summary>Builds one capture context with its production catalog unit.</summary>
    /// <param name="capture">The Adapter result.</param>
    /// <param name="captureUnitOverride">An alternate mapping for malformed-area tests.</param>
    /// <returns>The capture plus deterministic Host provenance.</returns>
    private static LiveCaptureContext BuildContext(
        IpcCaptureResultMessage capture,
        CaptureUnitDefinition? captureUnitOverride = null)
    {
        CaptureUnitDefinition unit = captureUnitOverride ?? LiveStateCatalog.Default.CaptureUnits.Single(
            candidate => candidate.Source == capture.Source && candidate.CaptureKey == capture.CaptureKey);
        var source = new AdapterCaptureSource(AdapterInstanceId.NewId(), 8);
        var adapterSnapshot = new AdapterAvailabilitySnapshot(
            AdapterAvailability.Available,
            source.InstanceId,
            capture.CorrelationId == 0,
            source.ConnectionGeneration);
        return new LiveCaptureContext(
            capture,
            source,
            unit,
            adapterSnapshot,
            capture.PlayContextId,
            12,
            new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));
    }

    /// <summary>Builds one available or unavailable private calendar sample.</summary>
    /// <param name="payload">The encoded private value.</param>
    /// <param name="availability">Whether the Adapter had authoritative backing values.</param>
    /// <param name="correlationId">The request correlation, or zero for a baseline.</param>
    /// <returns>The matching capture result.</returns>
    private static IpcCaptureResultMessage BuildCapture(
        byte[] payload,
        CaptureAvailability availability = CaptureAvailability.Available,
        ulong correlationId = 1) =>
        new(
            correlationId,
            CaptureSourceKind.Sample,
            (uint)CharacterSampleToken.GameTime,
            availability,
            PlayContextId.NewId(),
            payload);

    /// <summary>Asserts a normalized Snapshot and its exact capture provenance.</summary>
    /// <param name="call">The recorded application call.</param>
    /// <param name="value">The expected calendar or unavailable marker.</param>
    /// <param name="isBaseline">Whether this capture establishes a baseline.</param>
    /// <param name="context">The capture context to preserve.</param>
    private static void AssertApplyCall(
        ApplyCall call,
        GameTime? value,
        bool isBaseline,
        LiveCaptureContext context)
    {
        Assert.Equal(value, call.Value);
        Assert.Equal(GameTimeArea, call.AreaId);
        Assert.Equal(UpdateMode.Snapshot, call.Mode);
        Assert.Equal(isBaseline, call.IsBaseline);
        Assert.Equal(context.Source, call.Source);
        Assert.Equal(context.AdapterSnapshot, call.AdapterSnapshot);
        Assert.Equal(context.PlayContextId, call.PlayContextId);
        Assert.Equal(context.PlayContextGeneration, call.PlayContextGeneration);
        Assert.Equal(context.OccurredAt, call.OccurredAt);
    }

    /// <summary>Reads a canonical public game-time Snapshot fixture.</summary>
    /// <param name="fileName">The fixture under <c>protocol/fixtures/state</c>.</param>
    /// <returns>The parsed complete protocol message.</returns>
    private static JsonDocument ReadStateFixture(string fileName) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "protocol", "fixtures", "state", fileName)));

    /// <summary>Verifies that only the game-time sample is claimed by this handler.</summary>
    [Fact]
    public void SupportedCaptures_ListsOnlyGameTimeSample()
    {
        Fixture fixture = CreateReady();
        Assert.Equal(
            [(CaptureSourceKind.Sample, (uint)CharacterSampleToken.GameTime)],
            fixture.Handler.SupportedCaptures);
    }

    /// <summary>Verifies zero-based month normalization, fractional-hour minute derivation, and localized text preservation.</summary>
    [Fact]
    public void Handle_AvailableCapture_NormalizesCalendarFields()
    {
        using JsonDocument protocolFixture = ReadStateFixture("state-snapshot-game-time.json");
        JsonElement fixtureValue = protocolFixture.RootElement.GetProperty("payload")
            .GetProperty("data").GetProperty("value");
        Assert.Equal(201, fixtureValue.GetProperty("year").GetInt32());
        Assert.Equal(9, fixtureValue.GetProperty("month").GetInt32());
        Assert.Equal("Hearthfire", fixtureValue.GetProperty("monthName").GetString());
        Assert.Equal(17, fixtureValue.GetProperty("day").GetInt32());
        Assert.Equal(17, fixtureValue.GetProperty("hour").GetInt32());
        Assert.Equal(45, fixtureValue.GetProperty("minute").GetInt32());
        Fixture fixture = CreateReady();
        var capture = BuildCapture(EncodeGameTime(201.0f, 8.0f, 17.0f, 17.75f, "Hearthfire"), correlationId: 0);
        LiveCaptureContext context = BuildContext(capture);

        fixture.Handler.Handle(context);

        Assert.Collection(
            fixture.Application.ApplyCalls,
            call => AssertApplyCall(
                call, new GameTime(201, 9, "Hearthfire", 17, 17, 45), true, context));
    }

    /// <summary>Verifies the first and last raw months normalize to public months 1 and 12.</summary>
    [Fact]
    public void Handle_MonthBoundaries_NormalizesToOneThroughTwelve()
    {
        foreach ((float RawMonth, string MonthName, int PublicMonth) value in new[]
        {
            (0.0f, "Morning Star", 1),
            (11.0f, "Evening Star", 12),
        })
        {
            Fixture fixture = CreateReady();
            var capture = BuildCapture(
                EncodeGameTime(201.0f, value.RawMonth, 1.0f, 0.0f, value.MonthName));
            LiveCaptureContext context = BuildContext(capture);
            fixture.Handler.Handle(context);

            Assert.Collection(
                fixture.Application.ApplyCalls,
                call => AssertApplyCall(
                    call,
                    new GameTime(201, value.PublicMonth, value.MonthName, 1, 0, 0),
                    false,
                    context));
        }
    }

    /// <summary>Verifies missing authoritative calendar state maps to whole-domain unavailability.</summary>
    [Fact]
    public void Handle_UnavailableCapture_AppliesNullSnapshot()
    {
        using JsonDocument protocolFixture = ReadStateFixture("state-snapshot-game-time-unavailable.json");
        JsonElement fixtureValue = protocolFixture.RootElement.GetProperty("payload")
            .GetProperty("data").GetProperty("value");
        Assert.Equal(JsonValueKind.Null, fixtureValue.ValueKind);
        Fixture fixture = CreateReady();
        var capture = BuildCapture([], CaptureAvailability.Unavailable, correlationId: 0);
        LiveCaptureContext context = BuildContext(capture);

        fixture.Handler.Handle(context);

        Assert.Collection(
            fixture.Application.ApplyCalls,
            call => AssertApplyCall(call, null, true, context));
    }

    /// <summary>Verifies invalid backing values, malformed names, and incomplete payloads never publish partial time.</summary>
    [Fact]
    public void Handle_InvalidCalendarCapture_AppliesNothing()
    {
        byte[] valid = EncodeGameTime(201.0f, 8.0f, 17.0f, 17.75f, "Hearthfire");
        byte[] invalidUtf8 = [.. valid];
        invalidUtf8[16] = 1;
        invalidUtf8[17] = 0xFF;
        byte[] embeddedNul = [.. valid];
        embeddedNul[17] = 0;
        byte[] trailing = [.. valid, 0];
        byte[] missingName = [.. valid];
        missingName[16] = 0;
        var invalidCaptures = new[]
        {
            BuildCapture([]),
            BuildCapture(valid[..16]),
            BuildCapture(valid[..^1]),
            BuildCapture(invalidUtf8),
            BuildCapture(embeddedNul),
            BuildCapture(trailing),
            BuildCapture(missingName),
            BuildCapture(EncodeGameTime(float.NaN, 8.0f, 17.0f, 17.75f, "Hearthfire")),
            BuildCapture(EncodeGameTime(-1.0f, 8.0f, 17.0f, 17.75f, "Hearthfire")),
            BuildCapture(EncodeGameTime(2147483648.0f, 8.0f, 17.0f, 17.75f, "Hearthfire")),
            BuildCapture(EncodeGameTime(201.5f, 8.0f, 17.0f, 17.75f, "Hearthfire")),
            BuildCapture(EncodeGameTime(201.0f, 8.5f, 17.0f, 17.75f, "Hearthfire")),
            BuildCapture(EncodeGameTime(201.0f, 12.0f, 17.0f, 17.75f, "Hearthfire")),
            BuildCapture(EncodeGameTime(201.0f, 8.0f, 2.5f, 17.75f, "Hearthfire")),
            BuildCapture(EncodeGameTime(201.0f, 8.0f, 32.0f, 17.75f, "Hearthfire")),
            BuildCapture(EncodeGameTime(201.0f, 8.0f, 17.0f, -0.25f, "Hearthfire")),
            BuildCapture(EncodeGameTime(201.0f, 8.0f, 17.0f, 24.0f, "Hearthfire")),
            BuildCapture(EncodeGameTime(
                201.0f, 8.0f, 17.0f, 17.75f, new string('x', Constants.MaxGameMonthNameBytes + 1))),
            BuildCapture(
                EncodeGameTime(201.0f, 8.0f, 17.0f, 17.75f, "Hearthfire"),
                CaptureAvailability.Unavailable),
        };

        foreach (IpcCaptureResultMessage capture in invalidCaptures)
        {
            Fixture fixture = CreateReady();
            fixture.Handler.Handle(BuildContext(capture));
            Assert.Empty(fixture.Application.ApplyCalls);
        }
    }

    /// <summary>Verifies that malformed multi-area catalog mappings are ignored.</summary>
    [Fact]
    public void Handle_CaptureUnitWithWrongAreaCount_AppliesNothing()
    {
        CaptureUnitDefinition registered = LiveStateCatalog.Default.CaptureUnits.Single(
            candidate => candidate.CaptureKey == (uint)CharacterSampleToken.GameTime);
        CaptureUnitDefinition[] malformed =
        [
            registered with { StateAreas = [] },
            registered with { StateAreas = [GameTimeArea, GameTimeArea] },
        ];

        foreach (CaptureUnitDefinition unit in malformed)
        {
            Fixture fixture = CreateReady();
            fixture.Handler.Handle(BuildContext(
                BuildCapture(EncodeGameTime(201.0f, 8.0f, 17.0f, 17.75f, "Hearthfire")),
                unit));
            Assert.Empty(fixture.Application.ApplyCalls);
        }
    }
}
