using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DovahLink.Host.Adapter;
using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;
using DovahLink.Host.State;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.Adapter.Ipc;

/// <summary>Tests Character payload decoding, area mapping, and forwarding to shared application authority.</summary>
public class CharacterCaptureHandlerTests
{
    private static readonly StateAreaId VitalsArea = new(Constants.CharacterVitalsStateArea);
    private static readonly StateAreaId XpArea = new(Constants.CharacterXpStateArea);
    private static readonly StateAreaId LevelArea = new(Constants.CharacterLevelStateArea);
    private static readonly StateAreaId IdentityArea = new(Constants.CharacterIdentityStateArea);
    private static readonly StateAreaId SupernaturalTraitsArea = new(Constants.CharacterSupernaturalTraitsStateArea);

    /// <summary>Records one value sent to the shared application service.</summary>
    /// <param name="StateType">The generic publisher state type.</param>
    /// <param name="Publisher">The typed publisher supplied by the handler.</param>
    /// <param name="Mode">The canonical Snapshot or Event mode supplied by the handler.</param>
    /// <param name="AreaId">The destination state area.</param>
    /// <param name="Value">The decoded value or unavailable marker.</param>
    /// <param name="IsBaseline">Whether the handler marked the value as a resynchronization baseline.</param>
    /// <param name="Source">The exact adapter source forwarded by the handler.</param>
    /// <param name="AdapterSnapshot">The validated adapter availability snapshot.</param>
    /// <param name="PlayContextId">The validated play-context identity.</param>
    /// <param name="PlayContextGeneration">The validated play-context generation.</param>
    /// <param name="OccurredAt">The accepted capture timestamp.</param>
    private sealed record ApplyCall(
        Type StateType,
        object Publisher,
        UpdateMode Mode,
        StateAreaId AreaId,
        object? Value,
        bool IsBaseline,
        AdapterCaptureSource Source,
        AdapterAvailabilitySnapshot AdapterSnapshot,
        PlayContextId PlayContextId,
        long PlayContextGeneration,
        DateTimeOffset OccurredAt);

    /// <summary>A strict recorder for calls to shared Host authority.</summary>
    private sealed class RecordingLiveStateApplication : ILiveStateApplication
    {
        /// <summary>Every decoded value sent to the application service, in call order.</summary>
        public List<ApplyCall> ApplyCalls { get; } = [];

        /// <inheritdoc/>
        public void Apply<TState>(
            IStatePublisher<TState> publisher,
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
                typeof(TState),
                publisher,
                mode,
                areaId,
                value,
                isResynchronizationBaseline,
                source,
                adapterSnapshot,
                capturedPlayContextId,
                capturedPlayContextGeneration,
                occurredAt));
    }

    /// <summary>A publisher stub that fails if a handler bypasses the shared application service.</summary>
    /// <typeparam name="TState">The state value type represented by this publisher.</typeparam>
    private sealed class UnusedStatePublisher<TState> : IStatePublisher<TState>
    {
        /// <inheritdoc/>
        public bool TryGetCurrentValue(StateAreaId areaId, [MaybeNullWhen(false)] out TState value) =>
            throw new InvalidOperationException("Character handlers must apply values through ILiveStateApplication.");

        /// <inheritdoc/>
        public RevisionNumber CurrentRevision(StateAreaId areaId) =>
            throw new InvalidOperationException("Character handlers must apply values through ILiveStateApplication.");

        /// <inheritdoc/>
        public StateApplyResult Apply(
            AdapterInstanceId sourceInstanceId,
            long sourceConnectionGeneration,
            PlayContextId capturedPlayContextId,
            long capturedPlayContextGeneration,
            StateAreaId areaId,
            TState value) =>
            throw new InvalidOperationException("Character handlers must apply values through ILiveStateApplication.");

        /// <inheritdoc/>
        public StateApplyResult ApplyResynchronizationBaseline(
            IAdapterResynchronizationToken resynchronizationToken,
            PlayContextId capturedPlayContextId,
            long capturedPlayContextGeneration,
            StateAreaId areaId,
            TState value) =>
            throw new InvalidOperationException("Character handlers must apply values through ILiveStateApplication.");

        /// <inheritdoc/>
        public StateApplyResult ApplyEvent(
            AdapterInstanceId sourceInstanceId,
            long sourceConnectionGeneration,
            PlayContextId capturedPlayContextId,
            long capturedPlayContextGeneration,
            IAdapterResynchronizationToken? resynchronizationToken,
            StateAreaId areaId,
            TState value) =>
            throw new InvalidOperationException("Character handlers must apply values through ILiveStateApplication.");
    }

    /// <summary>The handler and test doubles used to observe its application calls.</summary>
    /// <param name="Handler">The Character handler under test.</param>
    /// <param name="Application">The recorder for decoded values.</param>
    /// <param name="VitalsPublisher">The strict publisher for coherent Vitals.</param>
    /// <param name="XpPublisher">The strict publisher for XP.</param>
    /// <param name="LevelPublisher">The strict publisher for level.</param>
    /// <param name="IdentityPublisher">The strict publisher for Character Identity.</param>
    /// <param name="SupernaturalTraitsPublisher">The strict publisher for supernatural traits.</param>
    private sealed record Fixture(
        CharacterCaptureHandler Handler,
        RecordingLiveStateApplication Application,
        IStatePublisher<CharacterVitals?> VitalsPublisher,
        IStatePublisher<float?> XpPublisher,
        IStatePublisher<ushort?> LevelPublisher,
        IStatePublisher<CharacterIdentity?> IdentityPublisher,
        IStatePublisher<CharacterSupernaturalTraits?> SupernaturalTraitsPublisher);

    /// <summary>Builds a handler with strict publishers and an application-call recorder.</summary>
    private static Fixture CreateReady()
    {
        var application = new RecordingLiveStateApplication();
        IStatePublisher<CharacterVitals?> vitalsPublisher = new UnusedStatePublisher<CharacterVitals?>();
        IStatePublisher<float?> xpPublisher = new UnusedStatePublisher<float?>();
        IStatePublisher<ushort?> levelPublisher = new UnusedStatePublisher<ushort?>();
        IStatePublisher<CharacterIdentity?> identityPublisher = new UnusedStatePublisher<CharacterIdentity?>();
        IStatePublisher<CharacterSupernaturalTraits?> supernaturalTraitsPublisher = new UnusedStatePublisher<CharacterSupernaturalTraits?>();
        var handler = new CharacterCaptureHandler(
            vitalsPublisher,
            xpPublisher,
            levelPublisher,
            identityPublisher,
            supernaturalTraitsPublisher,
            application);
        return new Fixture(
            handler,
            application,
            vitalsPublisher,
            xpPublisher,
            levelPublisher,
            identityPublisher,
            supernaturalTraitsPublisher);
    }

    /// <summary>Builds validated dispatch metadata for a capture in the production catalog.</summary>
    /// <param name="captureResult">The result to pair with its catalog unit.</param>
    /// <param name="captureUnitOverride">An alternate matching unit for malformed catalog-mapping cases.</param>
    /// <returns>The result, exact catalog unit, and deterministic authority metadata.</returns>
    private static LiveCaptureContext BuildContext(
        IpcCaptureResultMessage captureResult,
        CaptureUnitDefinition? captureUnitOverride = null)
    {
        CaptureUnitDefinition captureUnit = captureUnitOverride ?? LiveStateCatalog.Default.CaptureUnits.Single(
            candidate => candidate.Source == captureResult.Source && candidate.CaptureKey == captureResult.CaptureKey);
        var source = new AdapterCaptureSource(AdapterInstanceId.NewId(), 7);
        var adapterSnapshot = new AdapterAvailabilitySnapshot(
            AdapterAvailability.Available,
            source.InstanceId,
            captureResult.CorrelationId == 0,
            source.ConnectionGeneration);
        return new LiveCaptureContext(
            captureResult,
            source,
            captureUnit,
            adapterSnapshot,
            captureResult.PlayContextId,
            11,
            new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero));
    }

    /// <summary>Asserts that one handler application call preserves its publisher, value, mode, and capture authority.</summary>
    /// <param name="call">The recorded application call.</param>
    /// <param name="publisher">The expected typed publisher instance.</param>
    /// <param name="stateType">The expected generic state type.</param>
    /// <param name="mode">The expected publication mode.</param>
    /// <param name="areaId">The expected destination area.</param>
    /// <param name="value">The expected decoded value.</param>
    /// <param name="isBaseline">Whether the capture is expected to establish a baseline.</param>
    /// <param name="context">The validated context whose provenance must be preserved.</param>
    private static void AssertApplyCall(
        ApplyCall call,
        object publisher,
        Type stateType,
        UpdateMode mode,
        StateAreaId areaId,
        object? value,
        bool isBaseline,
        LiveCaptureContext context)
    {
        Assert.Equal(stateType, call.StateType);
        Assert.Same(publisher, call.Publisher);
        Assert.Equal(mode, call.Mode);
        Assert.Equal(areaId, call.AreaId);
        Assert.Equal(value, call.Value);
        Assert.Equal(isBaseline, call.IsBaseline);
        Assert.Equal(context.Source, call.Source);
        Assert.Equal(context.AdapterSnapshot, call.AdapterSnapshot);
        Assert.Equal(context.PlayContextId, call.PlayContextId);
        Assert.Equal(context.PlayContextGeneration, call.PlayContextGeneration);
        Assert.Equal(context.OccurredAt, call.OccurredAt);
    }

    /// <summary>Encodes current Health, Magicka, and Stamina followed by their maximums.</summary>
    /// <param name="health">The encoded health value.</param>
    /// <param name="magicka">The encoded magicka value.</param>
    /// <param name="stamina">The encoded stamina value.</param>
    /// <param name="healthMax">The encoded maximum health value.</param>
    /// <param name="magickaMax">The encoded maximum magicka value.</param>
    /// <param name="staminaMax">The encoded maximum stamina value.</param>
    /// <returns>The 24-byte little-endian sample.</returns>
    private static byte[] EncodeVitals(float health, float magicka, float stamina, float healthMax, float magickaMax, float staminaMax)
    {
        var bytes = new byte[24];
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(0, 4), health);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(4, 4), magicka);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(8, 4), stamina);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(12, 4), healthMax);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(16, 4), magickaMax);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(20, 4), staminaMax);
        return bytes;
    }

    /// <summary>Encodes one little-endian float.</summary>
    /// <param name="value">The value to encode.</param>
    /// <returns>The four-byte float payload.</returns>
    private static byte[] EncodeFloat(float value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(bytes, value);
        return bytes;
    }

    /// <summary>Encodes one little-endian 16-bit unsigned integer.</summary>
    /// <param name="value">The value to encode.</param>
    /// <returns>The two-byte level payload.</returns>
    private static byte[] EncodeUInt16(ushort value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        return bytes;
    }

    /// <summary>Encodes two length-prefixed UTF-8 strings using the Adapter's private identity layout.</summary>
    /// <param name="name">The player display name.</param>
    /// <param name="race">The identity-race display name.</param>
    /// <returns>The two-string private payload.</returns>
    private static byte[] EncodeIdentity(string name, string race)
    {
        byte[] nameBytes = Encoding.UTF8.GetBytes(name);
        byte[] raceBytes = Encoding.UTF8.GetBytes(race);
        var payload = new byte[2 + nameBytes.Length + raceBytes.Length];
        payload[0] = checked((byte)nameBytes.Length);
        nameBytes.AsSpan().CopyTo(payload.AsSpan(1));
        int raceLengthOffset = 1 + nameBytes.Length;
        payload[raceLengthOffset] = checked((byte)raceBytes.Length);
        raceBytes.AsSpan().CopyTo(payload.AsSpan(raceLengthOffset + 1));
        return payload;
    }

    /// <summary>Encodes three private boolean bytes in contract field order.</summary>
    /// <param name="isVampire">The vampire status value.</param>
    /// <param name="hasVampireLordForm">The Vampire Lord capability value.</param>
    /// <param name="hasWerewolfForm">The Beast Form capability value.</param>
    /// <returns>The three-byte supernatural-traits payload.</returns>
    private static byte[] EncodeSupernaturalTraits(bool isVampire, bool hasVampireLordForm, bool hasWerewolfForm) =>
        [
            isVampire ? (byte)1 : (byte)0,
            hasVampireLordForm ? (byte)1 : (byte)0,
            hasWerewolfForm ? (byte)1 : (byte)0,
        ];

    /// <summary>Reads a canonical public state fixture copied beside the test assembly.</summary>
    /// <param name="fileName">The fixture file under <c>protocol/fixtures/state</c>.</param>
    /// <returns>The parsed complete public message.</returns>
    private static JsonDocument ReadStateFixture(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "protocol", "fixtures", "state", fileName);
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    /// <summary>Verifies that the handler advertises all six Character capture identities.</summary>
    [Fact]
    public void SupportedCaptures_ListsTheCharacterCaptureSet()
    {
        Fixture fixture = CreateReady();

        Assert.Equal(
            new (CaptureSourceKind Source, uint CaptureKey)[]
            {
                (CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals),
                (CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp),
                (CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterIdentity),
                (CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterSupernaturalTraits),
                (CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterLevelBaseline),
                (CaptureSourceKind.Event, (uint)CharacterEventKey.CharacterLevelChanged),
            },
            fixture.Handler.SupportedCaptures);
    }

    /// <summary>Verifies that one Vitals sample maps all six finite values into one typed area application.</summary>
    [Fact]
    public void Handle_VitalsAvailable_AppliesOneCoherentArea()
    {
        using JsonDocument protocolFixture = ReadStateFixture("state-snapshot-character-vitals.json");
        JsonElement fixturePayload = protocolFixture.RootElement.GetProperty("payload");
        Assert.Equal(Constants.CharacterVitalsStateArea, fixturePayload.GetProperty("stateArea").GetString());
        Fixture fixture = CreateReady();
        var captureResult = new IpcCaptureResultMessage(
            1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals,
            CaptureAvailability.Available, PlayContextId.NewId(), EncodeVitals(327.0f, 71.0f, 100.0f, 410.0f, 220.0f, 300.0f));
        LiveCaptureContext context = BuildContext(captureResult);

        fixture.Handler.Handle(context);

        var expected = new CharacterVitals(
            new CharacterVital(327.0f, 410.0f),
            new CharacterVital(71.0f, 220.0f),
            new CharacterVital(100.0f, 300.0f));
        Assert.Collection(
            fixture.Application.ApplyCalls,
            call => AssertApplyCall(call, fixture.VitalsPublisher, typeof(CharacterVitals), UpdateMode.Snapshot, VitalsArea, expected, false, context));
    }

    /// <summary>Verifies that an unavailable Vitals sample marks the whole domain unavailable.</summary>
    [Fact]
    public void Handle_VitalsUnavailable_AppliesNullToEveryArea()
    {
        using JsonDocument protocolFixture = ReadStateFixture("state-snapshot-character-vitals-unavailable.json");
        JsonElement fixturePayload = protocolFixture.RootElement.GetProperty("payload");
        Assert.Equal(Constants.CharacterVitalsStateArea, fixturePayload.GetProperty("stateArea").GetString());
        Assert.Equal(JsonValueKind.Null, fixturePayload.GetProperty("data").GetProperty("value").ValueKind);
        Fixture fixture = CreateReady();
        var captureResult = new IpcCaptureResultMessage(
            1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals,
            CaptureAvailability.Unavailable, PlayContextId.NewId(), []);
        LiveCaptureContext context = BuildContext(captureResult);

        fixture.Handler.Handle(context);

        Assert.Collection(
            fixture.Application.ApplyCalls,
            call => AssertApplyCall(call, fixture.VitalsPublisher, typeof(CharacterVitals), UpdateMode.Snapshot, VitalsArea, null, false, context));
    }

    /// <summary>Verifies that finite maxima are preserved raw inside the coherent value.</summary>
    [Fact]
    public void Handle_VitalsNegativeMaximum_PreservesRawValue()
    {
        Fixture fixture = CreateReady();
        var captureResult = new IpcCaptureResultMessage(
            1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals,
            CaptureAvailability.Available, PlayContextId.NewId(), EncodeVitals(327.0f, 71.0f, 100.0f, -12.0f, 0.0f, 300.0f));
        LiveCaptureContext context = BuildContext(captureResult);

        fixture.Handler.Handle(context);

        CharacterVitals vitals = Assert.IsType<CharacterVitals>(fixture.Application.ApplyCalls[0].Value);
        Assert.Equal(-12.0f, vitals.Health.Max);
        Assert.Equal(0.0f, vitals.Magicka.Max);
    }

    /// <summary>Verifies that malformed, non-finite, or unavailable-with-payload Vitals captures apply no area.</summary>
    [Fact]
    public void Handle_VitalsMalformedCapture_AppliesNoArea()
    {
        IpcCaptureResultMessage[] invalidCaptures =
        [
            new(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals,
                CaptureAvailability.Available, PlayContextId.NewId(), new byte[10]),
            new(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals,
                CaptureAvailability.Available, PlayContextId.NewId(), EncodeVitals(93.4f, float.NaN, 100.0f, 410.0f, 220.0f, 300.0f)),
            new(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals,
                CaptureAvailability.Available, PlayContextId.NewId(), EncodeVitals(93.4f, 71.0f, 100.0f, float.NaN, 220.0f, 300.0f)),
            new(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals,
                CaptureAvailability.Unavailable, PlayContextId.NewId(), [1]),
        ];

        foreach (IpcCaptureResultMessage captureResult in invalidCaptures)
        {
            Fixture fixture = CreateReady();
            fixture.Handler.Handle(BuildContext(captureResult));

            Assert.Empty(fixture.Application.ApplyCalls);
        }
    }

    /// <summary>Verifies that XP available and unavailable payloads map to one Snapshot value.</summary>
    [Fact]
    public void Handle_XpAvailableAndUnavailable_AppliesFiniteValueOrNull()
    {
        Fixture fixture = CreateReady();
        var available = new IpcCaptureResultMessage(
            1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp,
            CaptureAvailability.Available, PlayContextId.NewId(), EncodeFloat(50.5f));
        var unavailable = new IpcCaptureResultMessage(
            2, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp,
            CaptureAvailability.Unavailable, available.PlayContextId, []);
        LiveCaptureContext availableContext = BuildContext(available);
        LiveCaptureContext unavailableContext = BuildContext(unavailable);

        fixture.Handler.Handle(availableContext);
        fixture.Handler.Handle(unavailableContext);

        Assert.Collection(
            fixture.Application.ApplyCalls,
            call => AssertApplyCall(call, fixture.XpPublisher, typeof(float?), UpdateMode.Snapshot, XpArea, 50.5f, false, availableContext),
            call => AssertApplyCall(call, fixture.XpPublisher, typeof(float?), UpdateMode.Snapshot, XpArea, null, false, unavailableContext));
    }

    /// <summary>Verifies that malformed or non-finite XP samples are dropped.</summary>
    [Fact]
    public void Handle_XpMalformedCapture_AppliesNothing()
    {
        IpcCaptureResultMessage[] invalidCaptures =
        [
            new(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp,
                CaptureAvailability.Available, PlayContextId.NewId(), new byte[3]),
            new(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp,
                CaptureAvailability.Available, PlayContextId.NewId(), EncodeFloat(float.PositiveInfinity)),
            new(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp,
                CaptureAvailability.Unavailable, PlayContextId.NewId(), EncodeFloat(50.0f)),
        ];

        foreach (IpcCaptureResultMessage captureResult in invalidCaptures)
        {
            Fixture fixture = CreateReady();
            fixture.Handler.Handle(BuildContext(captureResult));

            Assert.Empty(fixture.Application.ApplyCalls);
        }
    }

    /// <summary>Verifies a complete Unicode Identity observation maps to one typed Snapshot with its capture authority.</summary>
    [Fact]
    public void Handle_IdentityAvailable_AppliesCompleteUtf8Snapshot()
    {
        using JsonDocument protocolFixture = ReadStateFixture("state-snapshot-character-identity.json");
        JsonElement fixturePayload = protocolFixture.RootElement.GetProperty("payload");
        Assert.Equal(Constants.CharacterIdentityStateArea, fixturePayload.GetProperty("stateArea").GetString());
        Assert.Equal("Gonçalo", fixturePayload.GetProperty("data").GetProperty("value").GetProperty("name").GetString());
        Fixture fixture = CreateReady();
        var captureResult = new IpcCaptureResultMessage(
            1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterIdentity,
            CaptureAvailability.Available, PlayContextId.NewId(), EncodeIdentity("Gonçalo", "Nord"));
        LiveCaptureContext context = BuildContext(captureResult);

        fixture.Handler.Handle(context);

        var expected = new CharacterIdentity("Gonçalo", "Nord");
        Assert.Collection(
            fixture.Application.ApplyCalls,
            call => AssertApplyCall(call, fixture.IdentityPublisher, typeof(CharacterIdentity), UpdateMode.Snapshot, IdentityArea, expected, false, context));
    }

    /// <summary>Verifies missing Identity sources map to null for the whole Snapshot domain.</summary>
    [Fact]
    public void Handle_IdentityUnavailable_AppliesNullToTheWholeArea()
    {
        using JsonDocument protocolFixture = ReadStateFixture("state-snapshot-character-identity-unavailable.json");
        JsonElement fixturePayload = protocolFixture.RootElement.GetProperty("payload");
        Assert.Equal(Constants.CharacterIdentityStateArea, fixturePayload.GetProperty("stateArea").GetString());
        Assert.Equal(JsonValueKind.Null, fixturePayload.GetProperty("data").GetProperty("value").ValueKind);
        Fixture fixture = CreateReady();
        var captureResult = new IpcCaptureResultMessage(
            1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterIdentity,
            CaptureAvailability.Unavailable, PlayContextId.NewId(), []);
        LiveCaptureContext context = BuildContext(captureResult);

        fixture.Handler.Handle(context);

        Assert.Collection(
            fixture.Application.ApplyCalls,
            call => AssertApplyCall(call, fixture.IdentityPublisher, typeof(CharacterIdentity), UpdateMode.Snapshot, IdentityArea, null, false, context));
    }

    /// <summary>Verifies both Identity strings are accepted exactly at their byte limit without truncation.</summary>
    [Fact]
    public void Handle_IdentityAtMaximumByteLength_AppliesBothFullStrings()
    {
        string maximum = new('x', Constants.MaxCharacterIdentityStringBytes);
        string maximumUtf8 = string.Concat(Enumerable.Repeat("é", Constants.MaxCharacterIdentityStringBytes / 2));
        Fixture fixture = CreateReady();
        var captureResult = new IpcCaptureResultMessage(
            1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterIdentity,
            CaptureAvailability.Available, PlayContextId.NewId(), EncodeIdentity(maximum, maximum));
        LiveCaptureContext context = BuildContext(captureResult);

        fixture.Handler.Handle(context);

        Assert.Collection(
            fixture.Application.ApplyCalls,
            call => AssertApplyCall(call, fixture.IdentityPublisher, typeof(CharacterIdentity), UpdateMode.Snapshot, IdentityArea, new CharacterIdentity(maximum, maximum), false, context));

        Fixture unicodeFixture = CreateReady();
        var unicodeCapture = new IpcCaptureResultMessage(
            1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterIdentity,
            CaptureAvailability.Available, PlayContextId.NewId(), EncodeIdentity(maximumUtf8, maximumUtf8));
        LiveCaptureContext unicodeContext = BuildContext(unicodeCapture);
        unicodeFixture.Handler.Handle(unicodeContext);

        Assert.Collection(
            unicodeFixture.Application.ApplyCalls,
            call => AssertApplyCall(call, unicodeFixture.IdentityPublisher, typeof(CharacterIdentity), UpdateMode.Snapshot, IdentityArea, new CharacterIdentity(maximumUtf8, maximumUtf8), false, unicodeContext));
    }

    /// <summary>Verifies malformed lengths, text, truncation, trailing bytes, and unavailable payloads never apply Identity.</summary>
    [Fact]
    public void Handle_IdentityMalformedCapture_AppliesNothing()
    {
        IpcCaptureResultMessage[] invalidCaptures =
        [
            new(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterIdentity, CaptureAvailability.Available, PlayContextId.NewId(), []),
            new(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterIdentity, CaptureAvailability.Available, PlayContextId.NewId(), [0, (byte)'N', 1, (byte)'N']),
            new(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterIdentity, CaptureAvailability.Available, PlayContextId.NewId(), EncodeIdentity(new string('n', Constants.MaxCharacterIdentityStringBytes + 1), "Nord")),
            new(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterIdentity, CaptureAvailability.Available, PlayContextId.NewId(), EncodeIdentity("Name", new string('r', Constants.MaxCharacterIdentityStringBytes + 1))),
            new(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterIdentity, CaptureAvailability.Available, PlayContextId.NewId(), EncodeIdentity(new string('é', Constants.MaxCharacterIdentityStringBytes / 2 + 1), "Nord")),
            new(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterIdentity, CaptureAvailability.Available, PlayContextId.NewId(), [2, (byte)'N']),
            new(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterIdentity, CaptureAvailability.Available, PlayContextId.NewId(), [1, (byte)'N']),
            new(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterIdentity, CaptureAvailability.Available, PlayContextId.NewId(), [1, (byte)'N', 0, 0]),
            new(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterIdentity, CaptureAvailability.Available, PlayContextId.NewId(), [1, (byte)'N', 2, (byte)'N']),
            new(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterIdentity, CaptureAvailability.Available, PlayContextId.NewId(), [1, 0xC3, 1, (byte)'N']),
            new(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterIdentity, CaptureAvailability.Available, PlayContextId.NewId(), [1, (byte)'N', 1, 0xC3]),
            new(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterIdentity, CaptureAvailability.Available, PlayContextId.NewId(), [1, (byte)'N', 1, (byte)'N', 0]),
            new(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterIdentity, CaptureAvailability.Unavailable, PlayContextId.NewId(), EncodeIdentity("Name", "Nord")),
        ];

        foreach (IpcCaptureResultMessage captureResult in invalidCaptures)
        {
            Fixture fixture = CreateReady();
            fixture.Handler.Handle(BuildContext(captureResult));

            Assert.Empty(fixture.Application.ApplyCalls);
        }
    }

    /// <summary>Verifies available all-false supernatural traits are distinct from an unavailable Snapshot.</summary>
    [Fact]
    public void Handle_SupernaturalTraitsAvailableAndUnavailable_DistinguishesAllFalseFromNull()
    {
        using JsonDocument availableFixture = ReadStateFixture("state-snapshot-character-supernatural-traits.json");
        using JsonDocument unavailableFixture = ReadStateFixture("state-snapshot-character-supernatural-traits-unavailable.json");
        JsonElement availablePayload = availableFixture.RootElement.GetProperty("payload");
        JsonElement unavailablePayload = unavailableFixture.RootElement.GetProperty("payload");
        Assert.Equal(Constants.CharacterSupernaturalTraitsStateArea, availablePayload.GetProperty("stateArea").GetString());
        Assert.Equal(Constants.CharacterSupernaturalTraitsStateArea, unavailablePayload.GetProperty("stateArea").GetString());
        Fixture fixture = CreateReady();
        var available = new IpcCaptureResultMessage(
            1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterSupernaturalTraits,
            CaptureAvailability.Available, PlayContextId.NewId(), EncodeSupernaturalTraits(false, false, false));
        var unavailable = new IpcCaptureResultMessage(
            2, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterSupernaturalTraits,
            CaptureAvailability.Unavailable, available.PlayContextId, []);
        LiveCaptureContext availableContext = BuildContext(available);
        LiveCaptureContext unavailableContext = BuildContext(unavailable);

        fixture.Handler.Handle(availableContext);
        fixture.Handler.Handle(unavailableContext);

        Assert.Collection(
            fixture.Application.ApplyCalls,
            call => AssertApplyCall(call, fixture.SupernaturalTraitsPublisher, typeof(CharacterSupernaturalTraits), UpdateMode.Snapshot, SupernaturalTraitsArea, new CharacterSupernaturalTraits(false, false, false), false, availableContext),
            call => AssertApplyCall(call, fixture.SupernaturalTraitsPublisher, typeof(CharacterSupernaturalTraits), UpdateMode.Snapshot, SupernaturalTraitsArea, null, false, unavailableContext));
    }

    /// <summary>Verifies hybrid supernatural combinations preserve every independent predicate.</summary>
    /// <param name="isVampire">The encoded vampire status.</param>
    /// <param name="hasVampireLordForm">The encoded Vampire Lord capability.</param>
    /// <param name="hasWerewolfForm">The encoded Beast Form capability.</param>
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void Handle_SupernaturalTraitsHybrid_PreservesIndependentValues(bool isVampire, bool hasVampireLordForm, bool hasWerewolfForm)
    {
        Fixture fixture = CreateReady();
        var captureResult = new IpcCaptureResultMessage(
            1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterSupernaturalTraits,
            CaptureAvailability.Available, PlayContextId.NewId(), EncodeSupernaturalTraits(isVampire, hasVampireLordForm, hasWerewolfForm));
        LiveCaptureContext context = BuildContext(captureResult);

        fixture.Handler.Handle(context);

        var expected = new CharacterSupernaturalTraits(isVampire, hasVampireLordForm, hasWerewolfForm);
        Assert.Collection(
            fixture.Application.ApplyCalls,
            call => AssertApplyCall(call, fixture.SupernaturalTraitsPublisher, typeof(CharacterSupernaturalTraits), UpdateMode.Snapshot, SupernaturalTraitsArea, expected, false, context));
    }

    /// <summary>Verifies malformed boolean encodings and unavailable captures carrying bytes never apply supernatural state.</summary>
    [Fact]
    public void Handle_SupernaturalTraitsMalformedCapture_AppliesNothing()
    {
        IpcCaptureResultMessage[] invalidCaptures =
        [
            new(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterSupernaturalTraits, CaptureAvailability.Available, PlayContextId.NewId(), []),
            new(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterSupernaturalTraits, CaptureAvailability.Available, PlayContextId.NewId(), [0, 0]),
            new(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterSupernaturalTraits, CaptureAvailability.Available, PlayContextId.NewId(), [0, 0, 0, 0]),
            new(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterSupernaturalTraits, CaptureAvailability.Available, PlayContextId.NewId(), [2, 0, 0]),
            new(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterSupernaturalTraits, CaptureAvailability.Available, PlayContextId.NewId(), [0, 255, 0]),
            new(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterSupernaturalTraits, CaptureAvailability.Available, PlayContextId.NewId(), [0, 0, 2]),
            new(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterSupernaturalTraits, CaptureAvailability.Unavailable, PlayContextId.NewId(), [0, 0, 0]),
        ];

        foreach (IpcCaptureResultMessage captureResult in invalidCaptures)
        {
            Fixture fixture = CreateReady();
            fixture.Handler.Handle(BuildContext(captureResult));

            Assert.Empty(fixture.Application.ApplyCalls);
        }
    }

    /// <summary>Verifies ordinary Level samples, resynchronization baselines, and native Events keep distinct modes.</summary>
    [Fact]
    public void Handle_LevelSampleAndEvent_PreserveSnapshotBaselineAndEventSemantics()
    {
        Fixture fixture = CreateReady();
        var ordinarySample = new IpcCaptureResultMessage(
            5, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterLevelBaseline,
            CaptureAvailability.Available, PlayContextId.NewId(), EncodeUInt16(11));
        var baselineSample = new IpcCaptureResultMessage(
            0, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterLevelBaseline,
            CaptureAvailability.Available, PlayContextId.NewId(), EncodeUInt16(12));
        var levelEvent = new IpcCaptureResultMessage(
            0, CaptureSourceKind.Event, (uint)CharacterEventKey.CharacterLevelChanged,
            CaptureAvailability.Available, PlayContextId.NewId(), EncodeUInt16(13));
        var unavailableSample = new IpcCaptureResultMessage(
            6, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterLevelBaseline,
            CaptureAvailability.Unavailable, PlayContextId.NewId(), []);
        var unavailableEvent = new IpcCaptureResultMessage(
            0, CaptureSourceKind.Event, (uint)CharacterEventKey.CharacterLevelChanged,
            CaptureAvailability.Unavailable, PlayContextId.NewId(), []);
        LiveCaptureContext ordinaryContext = BuildContext(ordinarySample);
        LiveCaptureContext baselineContext = BuildContext(baselineSample);
        LiveCaptureContext eventContext = BuildContext(levelEvent);
        LiveCaptureContext unavailableContext = BuildContext(unavailableSample);
        LiveCaptureContext unavailableEventContext = BuildContext(unavailableEvent);

        fixture.Handler.Handle(ordinaryContext);
        fixture.Handler.Handle(baselineContext);
        fixture.Handler.Handle(eventContext);
        fixture.Handler.Handle(unavailableContext);
        fixture.Handler.Handle(unavailableEventContext);

        Assert.Collection(
            fixture.Application.ApplyCalls,
            call => AssertApplyCall(call, fixture.LevelPublisher, typeof(ushort?), UpdateMode.Snapshot, LevelArea, (ushort)11, false, ordinaryContext),
            call => AssertApplyCall(call, fixture.LevelPublisher, typeof(ushort?), UpdateMode.Snapshot, LevelArea, (ushort)12, true, baselineContext),
            call => AssertApplyCall(call, fixture.LevelPublisher, typeof(ushort?), UpdateMode.Event, LevelArea, (ushort)13, false, eventContext),
            call => AssertApplyCall(call, fixture.LevelPublisher, typeof(ushort?), UpdateMode.Snapshot, LevelArea, null, false, unavailableContext),
            call => AssertApplyCall(call, fixture.LevelPublisher, typeof(ushort?), UpdateMode.Event, LevelArea, null, false, unavailableEventContext));
    }

    /// <summary>Verifies that a malformed Level payload does not reach shared application authority.</summary>
    [Fact]
    public void Handle_LevelMalformedPayload_AppliesNothing()
    {
        Fixture fixture = CreateReady();
        var captureResult = new IpcCaptureResultMessage(
            1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterLevelBaseline,
            CaptureAvailability.Available, PlayContextId.NewId(), new byte[1]);

        fixture.Handler.Handle(BuildContext(captureResult));

        Assert.Empty(fixture.Application.ApplyCalls);
    }

    /// <summary>Verifies that mismatched state-area counts fail closed for every Character capture unit.</summary>
    [Fact]
    public void Handle_CaptureUnitWithInvalidAreaMapping_AppliesNothing()
    {
        Fixture fixture = CreateReady();
        CaptureUnitDefinition defaultVitals = LiveStateCatalog.Default.CaptureUnits.Single(
            unit => unit.Source == CaptureSourceKind.Sample
                && unit.CaptureKey == (uint)CharacterSampleToken.CharacterVitals);
        CaptureUnitDefinition defaultXp = LiveStateCatalog.Default.CaptureUnits.Single(
            unit => unit.Source == CaptureSourceKind.Sample
                && unit.CaptureKey == (uint)CharacterSampleToken.CharacterXp);
        CaptureUnitDefinition defaultIdentity = LiveStateCatalog.Default.CaptureUnits.Single(
            unit => unit.Source == CaptureSourceKind.Sample
                && unit.CaptureKey == (uint)CharacterSampleToken.CharacterIdentity);
        CaptureUnitDefinition defaultTraits = LiveStateCatalog.Default.CaptureUnits.Single(
            unit => unit.Source == CaptureSourceKind.Sample
                && unit.CaptureKey == (uint)CharacterSampleToken.CharacterSupernaturalTraits);
        CaptureUnitDefinition defaultLevel = LiveStateCatalog.Default.CaptureUnits.Single(
            unit => unit.Source == CaptureSourceKind.Sample
                && unit.CaptureKey == (uint)CharacterSampleToken.CharacterLevelBaseline);
        var invalidMappings = new (IpcCaptureResultMessage CaptureResult, CaptureUnitDefinition Unit)[]
        {
            (
                new IpcCaptureResultMessage(
                    1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals,
                    CaptureAvailability.Available, PlayContextId.NewId(), EncodeVitals(90.0f, 80.0f, 70.0f, 400.0f, 200.0f, 300.0f)),
                defaultVitals with { StateAreas = [] }),
            (
                new IpcCaptureResultMessage(
                    1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp,
                    CaptureAvailability.Available, PlayContextId.NewId(), EncodeFloat(50.0f)),
                defaultXp with { StateAreas = [] }),
            (
                new IpcCaptureResultMessage(
                    1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterIdentity,
                    CaptureAvailability.Available, PlayContextId.NewId(), EncodeIdentity("Name", "Nord")),
                defaultIdentity with { StateAreas = [] }),
            (
                new IpcCaptureResultMessage(
                    1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterSupernaturalTraits,
                    CaptureAvailability.Available, PlayContextId.NewId(), EncodeSupernaturalTraits(false, false, false)),
                defaultTraits with { StateAreas = [] }),
            (
                new IpcCaptureResultMessage(
                    1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterLevelBaseline,
                    CaptureAvailability.Available, PlayContextId.NewId(), EncodeUInt16(12)),
                defaultLevel with { StateAreas = [] }),
        };

        foreach ((IpcCaptureResultMessage captureResult, CaptureUnitDefinition unit) in invalidMappings)
        {
            fixture.Handler.Handle(BuildContext(captureResult, unit));
        }

        Assert.Empty(fixture.Application.ApplyCalls);
    }
}
