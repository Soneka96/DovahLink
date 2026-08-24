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
}
