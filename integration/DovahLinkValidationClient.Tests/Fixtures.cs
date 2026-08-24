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

    // ---- Pairing ----

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
        long? knownRevision = 2) =>
        new(stateArea, knownRevision);

    // ---- Rename ----

    /// <summary>Builds a representative RenameRequestPayload.</summary>
    /// <param name="displayName">The requested display name to use.</param>
    public static RenameRequestPayload BuildRenameRequestPayload(string displayName = "New Name") => new(displayName);
}
