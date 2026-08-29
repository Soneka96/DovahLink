namespace DovahLink.Host.Trust;

/// <summary>A single-use, expiring confirmation code for a global factory reset.</summary>
/// <param name="Code">The confirmation code that must be presented to complete the reset.</param>
/// <param name="ExpiresAtUtc">The UTC time after which this challenge can no longer be confirmed.</param>
public sealed record FactoryResetChallenge(string Code, DateTimeOffset ExpiresAtUtc);
