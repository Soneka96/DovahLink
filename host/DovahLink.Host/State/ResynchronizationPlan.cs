namespace DovahLink.Host.State;

/// <summary>The bounded native intents the Adapter executes to re-establish a fresh Host baseline.</summary>
/// <param name="PersistentEventKeys">Event keys to register before any baseline sample.</param>
/// <param name="BaselineSampleTokens">Sample tokens to capture after all event registrations.</param>
/// <remarks>The catalog builder removes duplicates in first-seen order within each key namespace.</remarks>
public sealed record ResynchronizationPlan(
    IReadOnlyList<uint> PersistentEventKeys,
    IReadOnlyList<uint> BaselineSampleTokens);
