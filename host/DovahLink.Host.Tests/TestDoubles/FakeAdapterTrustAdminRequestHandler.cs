using DovahLink.Host.Adapter.Ipc;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A configurable in-memory stand-in for <see cref="IAdapterTrustAdminRequestHandler"/>.</summary>
public sealed class FakeAdapterTrustAdminRequestHandler : IAdapterTrustAdminRequestHandler
{
    /// <summary>Every request this handler was asked to handle, in call order.</summary>
    public List<IpcTrustAdminRequestMessage> HandledRequests { get; } = [];

    /// <summary>The result text <see cref="HandleAsync"/> returns.</summary>
    public string Result { get; set; } = string.Empty;

    /// <summary>The cancellation tokens passed to <see cref="HandleAsync"/>, in call order.</summary>
    public List<CancellationToken> HandledCancellationTokens { get; } = [];

    /// <inheritdoc/>
    public Task<string> HandleAsync(IpcTrustAdminRequestMessage request, CancellationToken cancellationToken = default)
    {
        HandledRequests.Add(request);
        HandledCancellationTokens.Add(cancellationToken);
        return Task.FromResult(Result);
    }
}
