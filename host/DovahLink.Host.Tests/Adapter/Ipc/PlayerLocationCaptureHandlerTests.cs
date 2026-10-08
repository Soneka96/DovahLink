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

/// <summary>Tests complete-only player-location decoding, source selection, and Host application.</summary>
public class PlayerLocationCaptureHandlerTests
{
    /// <summary>The independently synchronized public area.</summary>
    private static readonly StateAreaId LocationArea = new(Constants.PlayerLocationStateArea);

    /// <summary>Records one typed value forwarded to shared Host authority.</summary>
    /// <param name="Value">The decoded value or unavailable marker.</param>
    /// <param name="AreaId">The public state area.</param>
    /// <param name="Mode">The public update mode.</param>
    /// <param name="IsBaseline">Whether the handler identified a resynchronization baseline.</param>
    /// <param name="Source">The Adapter instance and connection generation.</param>
    /// <param name="AdapterSnapshot">The validated Adapter availability snapshot.</param>
    /// <param name="PlayContextId">The capture's validated play context.</param>
    /// <param name="PlayContextGeneration">The validated play-context transition generation.</param>
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
        /// <summary>Every decoded value sent to the application service.</summary>
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

    /// <summary>The handler and strict collaborators used to observe its application calls.</summary>
    /// <param name="Handler">The player-location handler.</param>
    /// <param name="Application">The shared-application recorder.</param>
    private sealed record Fixture(
        PlayerLocationCaptureHandler Handler,
        RecordingLiveStateApplication Application);

    /// <summary>Builds the handler with an application recorder.</summary>
    /// <returns>The handler and its observable collaborators.</returns>
    private static Fixture CreateReady()
    {
        var application = new RecordingLiveStateApplication();
        return new Fixture(new PlayerLocationCaptureHandler(application), application);
    }

    /// <summary>Builds a location payload in the Adapter's private field order.</summary>
    /// <param name="cellId">The required current-cell FormID.</param>
    /// <param name="interior">Whether the cell is interior.</param>
    /// <param name="cellName">The optional cell name.</param>
    /// <param name="playerLocationId">The optional direct player-location FormID.</param>
    /// <param name="playerLocationName">The optional direct location name.</param>
    /// <param name="cellLocationId">The optional cell-location FormID.</param>
    /// <param name="cellLocationName">The optional cell-location name.</param>
    /// <param name="worldspaceId">The optional worldspace FormID.</param>
    /// <param name="worldspaceName">The optional worldspace name.</param>
    /// <returns>The bounded little-endian test payload.</returns>
    private static byte[] EncodeLocation(
        uint cellId = 10,
        bool interior = false,
        string? cellName = "WhiterunWorld",
        uint playerLocationId = 20,
        string? playerLocationName = "Whiterun",
        uint cellLocationId = 30,
        string? cellLocationName = "Whiterun",
        uint worldspaceId = 40,
        string? worldspaceName = "Skyrim")
    {
        using var payload = new MemoryStream();
        WriteUInt32(payload, cellId);
        payload.WriteByte(interior ? (byte)1 : (byte)0);
        WriteName(payload, cellName);
        WriteUInt32(payload, playerLocationId);
        WriteName(payload, playerLocationName);
        WriteUInt32(payload, cellLocationId);
        WriteName(payload, cellLocationName);
        WriteUInt32(payload, worldspaceId);
        WriteName(payload, worldspaceName);
        return payload.ToArray();
    }

    /// <summary>Writes one little-endian runtime FormID.</summary>
    /// <param name="payload">The destination buffer.</param>
    /// <param name="value">The FormID to append.</param>
    private static void WriteUInt32(Stream payload, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        payload.Write(bytes);
    }

    /// <summary>Writes one optional length-prefixed UTF-8 name.</summary>
    /// <param name="payload">The destination buffer.</param>
    /// <param name="value">The optional display name.</param>
    private static void WriteName(Stream payload, string? value)
    {
        byte[] bytes = value is null ? [] : Encoding.UTF8.GetBytes(value);
        payload.WriteByte(checked((byte)bytes.Length));
        payload.Write(bytes);
    }

    /// <summary>Builds a capture context with a catalog-matched location sample.</summary>
    /// <param name="capture">The adapter capture result.</param>
    /// <param name="captureUnitOverride">An alternate capture mapping used to test malformed area declarations.</param>
    /// <returns>The capture plus deterministic Host provenance.</returns>
    private static LiveCaptureContext BuildContext(
        IpcCaptureResultMessage capture,
        CaptureUnitDefinition? captureUnitOverride = null)
    {
        CaptureUnitDefinition unit = captureUnitOverride ?? LiveStateCatalog.Default.CaptureUnits.Single(
            candidate => candidate.Source == capture.Source && candidate.CaptureKey == capture.CaptureKey);
        var source = new AdapterCaptureSource(AdapterInstanceId.NewId(), 7);
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
            11,
            new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero));
    }

    /// <summary>Creates one available or unavailable private location capture.</summary>
    /// <param name="payload">The encoded private value.</param>
    /// <param name="availability">Whether the value is available.</param>
    /// <param name="correlationId">The request correlation, or zero for a resynchronization baseline.</param>
    /// <returns>The matching capture result.</returns>
    private static IpcCaptureResultMessage BuildCapture(
        byte[] payload,
        CaptureAvailability availability = CaptureAvailability.Available,
        ulong correlationId = 1) =>
        new(
            correlationId,
            CaptureSourceKind.Sample,
            (uint)CharacterSampleToken.PlayerLocation,
            availability,
            PlayContextId.NewId(),
            payload);

    /// <summary>Asserts that one location application call is a complete Snapshot .</summary>
    /// <param name="call">The recorded application call.</param>
    /// <param name="value">The expected typed value or unavailable marker.</param>
    /// <param name="isBaseline">Whether the capture is an identified baseline.</param>
    /// <param name="context">The validated provenance that must be preserved.</param>
    private static void AssertApplyCall(
        ApplyCall call,
        PlayerLocation? value,
        bool isBaseline,
        LiveCaptureContext context)
    {
        Assert.Equal(value, call.Value);
        Assert.Equal(LocationArea, call.AreaId);
        Assert.Equal(UpdateMode.Snapshot, call.Mode);
        Assert.Equal(isBaseline, call.IsBaseline);
        Assert.Equal(context.Source, call.Source);
        Assert.Equal(context.AdapterSnapshot, call.AdapterSnapshot);
        Assert.Equal(context.PlayContextId, call.PlayContextId);
        Assert.Equal(context.PlayContextGeneration, call.PlayContextGeneration);
        Assert.Equal(context.OccurredAt, call.OccurredAt);
    }

    /// <summary>Reads one canonical public Snapshot fixture.</summary>
    /// <param name="fileName">The fixture under <c>protocol/fixtures/state</c>.</param>
    /// <returns>The complete parsed protocol message.</returns>
    private static JsonDocument ReadStateFixture(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "protocol", "fixtures", "state", fileName);
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    /// <summary>Verifies that only the one player-location sample is claimed.</summary>
    [Fact]
    public void SupportedCaptures_ListsOnlyPlayerLocationSample()
    {
        Fixture fixture = CreateReady();

        Assert.Equal(
            [(CaptureSourceKind.Sample, (uint)CharacterSampleToken.PlayerLocation)],
            fixture.Handler.SupportedCaptures);
    }

    /// <summary>Verifies that the direct PlayerCharacter location takes precedence when both sources exist.</summary>
    [Fact]
    public void Handle_AvailableCapture_PrefersPlayerLocationAndPreservesOtherFacts()
    {
        using JsonDocument protocolFixture = ReadStateFixture("state-snapshot-player-location.json");
        JsonElement fixtureValue = protocolFixture.RootElement
            .GetProperty("payload")
            .GetProperty("data")
            .GetProperty("value");
        Assert.Equal("Whiterun", fixtureValue.GetProperty("locationName").GetString());
        Assert.Equal("Skyrim", fixtureValue.GetProperty("worldspaceName").GetString());
        Assert.Equal(123456u, fixtureValue.GetProperty("cellId").GetUInt32());
        Assert.Equal("exterior", fixtureValue.GetProperty("cellKind").GetString());
        Assert.Equal("WhiterunWorld", fixtureValue.GetProperty("cellName").GetString());
        Assert.Equal(98765u, fixtureValue.GetProperty("locationId").GetUInt32());
        Assert.Equal(1u, fixtureValue.GetProperty("worldspaceId").GetUInt32());
        Fixture fixture = CreateReady();
        var capture = BuildCapture(EncodeLocation(
            playerLocationName: "Source direct",
            cellLocationName: "Source cell"));

        LiveCaptureContext context = BuildContext(capture);
        fixture.Handler.Handle(context);

        Assert.Collection(
            fixture.Application.ApplyCalls,
            call => AssertApplyCall(
                call,
                new PlayerLocation(
                    10,
                    PlayerLocationCellKind.Exterior,
                    "WhiterunWorld",
                    20,
                    "Source direct",
                    40,
                    "Skyrim"),
                isBaseline: false,
                context: context));
    }

    /// <summary>Verifies that the cell location supplies the public location when the direct source is absent.</summary>
    [Fact]
    public void Handle_WithoutPlayerLocation_UsesCellLocation()
    {
        Fixture fixture = CreateReady();
        var capture = BuildCapture(EncodeLocation(playerLocationId: 0, playerLocationName: null));

        LiveCaptureContext context = BuildContext(capture);
        fixture.Handler.Handle(context);

        Assert.Collection(
            fixture.Application.ApplyCalls,
            call => AssertApplyCall(
                call,
                new PlayerLocation(10, PlayerLocationCellKind.Exterior, "WhiterunWorld", 30, "Whiterun", 40, "Skyrim"),
                isBaseline: false,
                context: context));
    }

    /// <summary>Verifies that a present but unnamed direct location does not borrow another source's name.</summary>
    [Fact]
    public void Handle_DirectLocationWithoutName_KeepsLocationNameNull()
    {
        Fixture fixture = CreateReady();
        var capture = BuildCapture(EncodeLocation(
            playerLocationName: null,
            cellLocationName: "Other source name"));
        LiveCaptureContext context = BuildContext(capture);

        fixture.Handler.Handle(context);

        Assert.Collection(
            fixture.Application.ApplyCalls,
            call => AssertApplyCall(
                call,
                new PlayerLocation(10, PlayerLocationCellKind.Exterior, "WhiterunWorld", 20, null, 40, "Skyrim"),
                isBaseline: false,
                context: context));
    }

    /// <summary>Verifies that localized UTF-8 display names survive private decoding and Host mapping.</summary>
    [Fact]
    public void Handle_LocalizedNames_PreservesUtf8Text()
    {
        Fixture fixture = CreateReady();
        var capture = BuildCapture(EncodeLocation(
            cellName: "Monastère du lac",
            playerLocationName: "Crête de l’ours",
            cellLocationName: "La grotte",
            worldspaceName: "Solitude"));
        LiveCaptureContext context = BuildContext(capture);

        fixture.Handler.Handle(context);

        Assert.Collection(
            fixture.Application.ApplyCalls,
            call => AssertApplyCall(
                call,
                new PlayerLocation(
                    10,
                    PlayerLocationCellKind.Exterior,
                    "Monastère du lac",
                    20,
                    "Crête de l’ours",
                    40,
                    "Solitude"),
                isBaseline: false,
                context: context));
    }

    /// <summary>Verifies that a valid unnamed cell stays available without fabricated location or worldspace values.</summary>
    [Fact]
    public void Handle_UnnamedCellWithNoLocationOrWorldspace_RemainsAvailable()
    {
        Fixture fixture = CreateReady();
        var capture = BuildCapture(EncodeLocation(
            interior: true,
            cellName: null,
            playerLocationId: 0,
            playerLocationName: null,
            cellLocationId: 0,
            cellLocationName: null,
            worldspaceId: 0,
            worldspaceName: null));

        LiveCaptureContext context = BuildContext(capture);
        fixture.Handler.Handle(context);

        Assert.Collection(
            fixture.Application.ApplyCalls,
            call => AssertApplyCall(
                call,
                new PlayerLocation(10, PlayerLocationCellKind.Interior, null, null, null, null, null),
                isBaseline: false,
                context: context));
    }

    /// <summary>Verifies that an unavailable required player/cell capture maps to null for the whole Snapshot area.</summary>
    [Fact]
    public void Handle_UnavailableCapture_AppliesNullSnapshot()
    {
        using JsonDocument protocolFixture = ReadStateFixture("state-snapshot-player-location-unavailable.json");
        JsonElement fixtureValue = protocolFixture.RootElement
            .GetProperty("payload")
            .GetProperty("data")
            .GetProperty("value");
        Assert.Equal(JsonValueKind.Null, fixtureValue.ValueKind);
        Fixture fixture = CreateReady();
        var capture = BuildCapture([], CaptureAvailability.Unavailable, correlationId: 0);

        LiveCaptureContext context = BuildContext(capture);
        fixture.Handler.Handle(context);

        Assert.Collection(
            fixture.Application.ApplyCalls,
            call => AssertApplyCall(call, null, isBaseline: true, context: context));
    }

    /// <summary>Verifies that malformed or inconsistent captures never publish partial state.</summary>
    [Fact]
    public void Handle_MalformedCapture_AppliesNothing()
    {
        byte[] valid = EncodeLocation();
        byte[] invalidCellKind = [.. valid];
        invalidCellKind[4] = 2;
        byte[] missingCell = EncodeLocation(cellId: 0);
        byte[] orphanedLocationName = EncodeLocation(playerLocationId: 0);
        byte[] orphanedCellLocationName = EncodeLocation(cellLocationId: 0);
        byte[] orphanedWorldspaceName = EncodeLocation(worldspaceId: 0);
        byte[] oversizedName = EncodeLocation(cellName: new string('x', Constants.MaxPlayerLocationNameBytes + 1));
        byte[] invalidCellNameUtf8 = [.. valid];
        invalidCellNameUtf8[6] = 0xFF;
        byte[] invalidPlayerLocationNameUtf8 = [.. valid];
        invalidPlayerLocationNameUtf8[24] = 0xFF;
        byte[] invalidCellLocationNameUtf8 = [.. valid];
        invalidCellLocationNameUtf8[37] = 0xFF;
        byte[] invalidWorldspaceNameUtf8 = [.. valid];
        invalidWorldspaceNameUtf8[50] = 0xFF;
        byte[] embeddedNullCellName = [.. valid];
        embeddedNullCellName[6] = 0;
        byte[] embeddedNullPlayerLocationName = [.. valid];
        embeddedNullPlayerLocationName[24] = 0;
        byte[] embeddedNullCellLocationName = [.. valid];
        embeddedNullCellLocationName[37] = 0;
        byte[] embeddedNullWorldspaceName = [.. valid];
        embeddedNullWorldspaceName[50] = 0;
        byte[] trailing = [.. valid, 0];
        var invalidCaptures = new[]
        {
            BuildCapture([]),
            BuildCapture(valid[..3]),
            BuildCapture(valid[..4]),
            BuildCapture(valid[..8]),
            BuildCapture(valid[..19]),
            BuildCapture(valid[..25]),
            BuildCapture(valid[..38]),
            BuildCapture(valid[..51]),
            BuildCapture(invalidCellKind),
            BuildCapture(missingCell),
            BuildCapture(orphanedLocationName),
            BuildCapture(orphanedCellLocationName),
            BuildCapture(orphanedWorldspaceName),
            BuildCapture(oversizedName),
            BuildCapture(invalidCellNameUtf8),
            BuildCapture(invalidPlayerLocationNameUtf8),
            BuildCapture(invalidCellLocationNameUtf8),
            BuildCapture(invalidWorldspaceNameUtf8),
            BuildCapture(embeddedNullCellName),
            BuildCapture(embeddedNullPlayerLocationName),
            BuildCapture(embeddedNullCellLocationName),
            BuildCapture(embeddedNullWorldspaceName),
            BuildCapture(trailing),
            BuildCapture(EncodeLocation(), CaptureAvailability.Unavailable),
        };

        foreach (IpcCaptureResultMessage capture in invalidCaptures)
        {
            Fixture fixture = CreateReady();
            fixture.Handler.Handle(BuildContext(capture));
            Assert.Empty(fixture.Application.ApplyCalls);
        }
    }

    /// <summary>Verifies that a capture is ignored when its catalog mapping is not one public area.</summary>
    [Fact]
    public void Handle_CaptureUnitWithWrongAreaCount_AppliesNothing()
    {
        CaptureUnitDefinition registeredUnit = LiveStateCatalog.Default.CaptureUnits.Single(
            candidate => candidate.CaptureKey == (uint)CharacterSampleToken.PlayerLocation);
        CaptureUnitDefinition[] malformedUnits =
        [
            registeredUnit with { StateAreas = [] },
            registeredUnit with { StateAreas = [LocationArea, LocationArea] },
        ];

        foreach (CaptureUnitDefinition malformedUnit in malformedUnits)
        {
            Fixture fixture = CreateReady();
            var capture = BuildCapture(EncodeLocation());
            fixture.Handler.Handle(BuildContext(capture, malformedUnit));
            Assert.Empty(fixture.Application.ApplyCalls);
        }
    }
}
