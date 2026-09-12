namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A controllable stand-in for <see cref="IHostSettingsProvider"/> that returns a fixed, test-supplied settings value.</summary>
public sealed class FakeHostSettingsProvider : IHostSettingsProvider
{
    /// <summary>The value <see cref="Load"/> returns.</summary>
    public HostSettings Settings { get; set; } = new(Constants.MaxActiveSessions);

    /// <inheritdoc/>
    public HostSettings Load() => Settings;
}
