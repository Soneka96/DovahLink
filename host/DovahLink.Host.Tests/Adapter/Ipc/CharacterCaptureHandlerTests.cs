using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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
    private sealed record Fixture(
        CharacterCaptureHandler Handler,
        RecordingLiveStateApplication Application,
        IStatePublisher<CharacterVitals?> VitalsPublisher,
        IStatePublisher<float?> XpPublisher,
        IStatePublisher<ushort?> LevelPublisher);

    /// <summary>Builds a handler with strict publishers and an application-call recorder.</summary>
    private static Fixture CreateReady()
    {
        var application = new RecordingLiveStateApplication();
        IStatePublisher<CharacterVitals?> vitalsPublisher = new UnusedStatePublisher<CharacterVitals?>();
        IStatePublisher<float?> xpPublisher = new UnusedStatePublisher<float?>();
        IStatePublisher<ushort?> levelPublisher = new UnusedStatePublisher<ushort?>();
        var handler = new CharacterCaptureHandler(vitalsPublisher, xpPublisher, levelPublisher, application);
        return new Fixture(handler, application, vitalsPublisher, xpPublisher, levelPublisher);
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

    /// <summary>Reads a canonical public state fixture copied beside the test assembly.</summary>
    /// <param name="fileName">The fixture file under <c>protocol/fixtures/state</c>.</param>
    /// <returns>The parsed complete public message.</returns>
    private static JsonDocument ReadStateFixture(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "protocol", "fixtures", "state", fileName);
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    /// <summary>Verifies that the handler advertises exactly its four Character capture identities.</summary>
    [Fact]
    public void SupportedCaptures_ListsTheCharacterCaptureSet()
    {
        Fixture fixture = CreateReady();

        Assert.Equal(
            new (CaptureSourceKind Source, uint CaptureKey)[]
            {
                (CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals),
                (CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp),
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

    /// <summary>Verifies that mismatched state-area counts fail closed for Vitals, XP, and Level units.</summary>
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
