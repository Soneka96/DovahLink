using DovahLink.Host.Process;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A controllable stand-in for <see cref="IHostRendezvousPublisher"/> recording every call.</summary>
public sealed class FakeHostRendezvousPublisher : IHostRendezvousPublisher
{
    /// <summary>Every <see cref="Publish"/> call's port, in call order.</summary>
    public List<int> PublishedPorts { get; } = [];

    /// <inheritdoc/>
    public void Publish(int port, byte[] peerProofToken, byte[] hostProofKey) => PublishedPorts.Add(port);
}
