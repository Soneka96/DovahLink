using System.Text.Json.Nodes;

namespace DovahLinkValidationClient.Tests;

/// <summary>
/// Builds representative test values for canonical protocol payloads, grouped by area. Each
/// builder defaults every parameter to one representative value; a test that wants the default
/// calls it with no arguments, and a test that needs one field different overrides only that
/// parameter.
/// </summary>
public static class Fixtures
{
    // ---- Connection ----

    /// <summary>Builds a representative HelloAuthPayload.</summary>
    /// <param name="method">The auth method to use.</param>
    /// <param name="token">The auth token to use, or <see langword="null"/> to omit it.</param>
    public static HelloAuthPayload BuildHelloAuthPayload(
        string method = "one_time_local_token",
        string? token = "redacted-in-documentation") =>
        new(method, token);

    /// <summary>Builds a representative HelloPayload.</summary>
    /// <param name="clientId">The client identifier to use.</param>
    /// <param name="auth">The auth payload to use, or <see langword="null"/> for a representative
    /// default built via <see cref="BuildHelloAuthPayload"/>.</param>
    public static HelloPayload BuildHelloPayload(string clientId = "client-1", HelloAuthPayload? auth = null) =>
        new(clientId, auth ?? BuildHelloAuthPayload());

    /// <summary>Builds a representative HelloAckPayload.</summary>
    /// <param name="bridgeVersion">The bridge version to use.</param>
    /// <param name="clientIdentityKind">The client identity kind to use.</param>
    public static HelloAckPayload BuildHelloAckPayload(
        string bridgeVersion = "0.3.2",
        string clientIdentityKind = "paired") =>
        new(bridgeVersion, clientIdentityKind);

    // ---- Pairing ----

    /// <summary>Builds a representative PairingStatusPayload.</summary>
    /// <param name="state">The pairing state to use.</param>
    /// <param name="expiresInSeconds">The remaining code validity to use.</param>
    public static PairingStatusPayload BuildPairingStatusPayload(
        string state = "available",
        int? expiresInSeconds = 300) =>
        new(state, expiresInSeconds);

    /// <summary>Builds a representative PairingConfirmPayload.</summary>
    /// <param name="code">The six-digit code to use.</param>
    /// <param name="displayName">The display name to use.</param>
    public static PairingConfirmPayload BuildPairingConfirmPayload(
        string code = "123456",
        string? displayName = "My PC") =>
        new(code, displayName);

    /// <summary>Builds a representative PairingAckPayload.</summary>
    /// <param name="credential">The credential to use.</param>
    public static PairingAckPayload BuildPairingAckPayload(string credential = "a1b2c3d4e5f6") => new(credential);

    /// <summary>Builds a representative PairingOutcomePayload.</summary>
    /// <param name="outcome">The outcome to use.</param>
    /// <param name="credential">The credential to use.</param>
    /// <param name="shortId">The short ID to use.</param>
    /// <param name="displayName">The display name to use.</param>
    /// <param name="retryAfterSeconds">The retry-after seconds to use.</param>
    public static PairingOutcomePayload BuildPairingOutcomePayload(
        string outcome = "trusted",
        string? credential = "a1b2c3d4e5f6",
        string? shortId = "12345",
        string? displayName = "My PC",
        int? retryAfterSeconds = null) =>
        new(outcome, credential, shortId, displayName, retryAfterSeconds);

    // ---- Capabilities ----

    /// <summary>Builds a representative Capability.</summary>
    /// <param name="id">The capability identifier to use.</param>
    /// <param name="version">The capability version to use.</param>
    public static Capability BuildCapability(string id = "state.inventory", int version = 1) => new(id, version);

    /// <summary>Builds a representative CapabilitiesPayload.</summary>
    /// <param name="capabilities">The capabilities to use, or <see langword="null"/> for an empty
    /// list -- the only currently registered shape.</param>
    public static CapabilitiesPayload BuildCapabilitiesPayload(IReadOnlyList<Capability>? capabilities = null) =>
        new(capabilities ?? []);

    // ---- Subscription control ----

    /// <summary>Builds a representative SubscribePayload.</summary>
    /// <param name="stateAreas">The state areas to request, or <see langword="null"/> for one
    /// representative area.</param>
    public static SubscribePayload BuildSubscribePayload(IReadOnlyList<string>? stateAreas = null) =>
        new(stateAreas ?? ["example_area"]);

    /// <summary>Builds a representative SnapshotRequestPayload.</summary>
    /// <param name="stateArea">The state area to use.</param>
    /// <param name="knownRevision">The known revision to use, or <see langword="null"/> to omit it.</param>
    public static SnapshotRequestPayload BuildSnapshotRequestPayload(
        string stateArea = "example_area",
        int? knownRevision = 2) =>
        new(stateArea, knownRevision);

    /// <summary>Builds a representative SubscriptionAckPayload.</summary>
    /// <param name="acceptedStateAreas">The accepted state areas to use, or <see langword="null"/>
    /// for an empty list -- the only currently reachable shape.</param>
    /// <param name="rejectedStateAreas">The rejected state areas to use, or <see langword="null"/>
    /// for one representative rejected area.</param>
    public static SubscriptionAckPayload BuildSubscriptionAckPayload(
        IReadOnlyList<string>? acceptedStateAreas = null,
        IReadOnlyList<string>? rejectedStateAreas = null) =>
        new(acceptedStateAreas ?? [], rejectedStateAreas ?? ["example_area"]);

    // ---- Rename ----

    /// <summary>Builds a representative RenameRequestPayload.</summary>
    /// <param name="displayName">The requested display name to use.</param>
    public static RenameRequestPayload BuildRenameRequestPayload(string displayName = "New Name") => new(displayName);

    /// <summary>Builds a representative RenameOutcomePayload.</summary>
    /// <param name="outcome">The outcome to use.</param>
    /// <param name="displayName">The display name to use.</param>
    public static RenameOutcomePayload BuildRenameOutcomePayload(
        string outcome = "renamed",
        string? displayName = "New Name") =>
        new(outcome, displayName);

    // ---- State ----

    /// <summary>Builds a representative StateSnapshotPayload.</summary>
    /// <param name="stateArea">The state area to use.</param>
    /// <param name="revision">The revision to use.</param>
    /// <param name="occurredAt">The timestamp to use.</param>
    /// <param name="data">The snapshot data to use, or <see langword="null"/> for a representative
    /// default.</param>
    public static StateSnapshotPayload BuildStateSnapshotPayload(
        string stateArea = "example_area",
        int revision = 1,
        string occurredAt = "2026-08-11T12:00:00Z",
        JsonObject? data = null) =>
        new(stateArea, revision, occurredAt, data ?? new JsonObject { ["value"] = 12 });

    /// <summary>Builds a representative StateEventPayload.</summary>
    /// <param name="stateArea">The state area to use.</param>
    /// <param name="baseRevision">The base revision to use.</param>
    /// <param name="revision">The revision to use.</param>
    /// <param name="occurredAt">The timestamp to use.</param>
    /// <param name="data">The event data to use, or <see langword="null"/> for a representative
    /// default.</param>
    public static StateEventPayload BuildStateEventPayload(
        string stateArea = "example_area",
        int baseRevision = 1,
        int revision = 2,
        string occurredAt = "2026-08-11T12:00:02Z",
        JsonObject? data = null) =>
        new(stateArea, baseRevision, revision, occurredAt, data ?? new JsonObject { ["value"] = 13 });

    // ---- Error and invalidation ----

    /// <summary>Builds a representative ErrorPayload.</summary>
    /// <param name="code">The error code to use.</param>
    /// <param name="message">The diagnostic message to use.</param>
    /// <param name="retryable">Whether the operation is retryable.</param>
    /// <param name="details">The structured details to use.</param>
    public static ErrorPayload BuildErrorPayload(
        string code = "unauthenticated",
        string message = "Token validation failed",
        bool retryable = false,
        JsonNode? details = null) =>
        new(code, message, retryable, details);

    /// <summary>Builds a representative SessionInvalidatedPayload.</summary>
    /// <param name="reason">The invalidation reason to use.</param>
    public static SessionInvalidatedPayload BuildSessionInvalidatedPayload(string reason = "revoked") => new(reason);
}
