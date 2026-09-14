using DovahLink.Host.Process;
using DovahLink.Host.Time;

namespace DovahLink.Host.Adapter.Ipc;

/// <summary>Builds one fresh connection-owned graph for each accepted adapter-IPC stream.</summary>
public interface IAdapterConnectionFactory
{
    /// <summary>
    /// Builds a fresh <see cref="AdapterIpcConnection"/>/<see cref="AdapterIpcSession"/> pair for
    /// <paramref name="stream"/>. Every collaborator this method constructs is connection-owned and
    /// never shared across two accepted connections; every collaborator this factory itself was
    /// constructed with is a Host-lifetime singleton shared by every connection.
    /// </summary>
    /// <param name="stream">The accepted connection's own transport stream.</param>
    /// <returns>A connection-owned <see cref="IAdapterIpcConnection"/>, ready to run.</returns>
    IAdapterIpcConnection Create(Stream stream);
}

/// <summary>See <see cref="IAdapterConnectionFactory"/>.</summary>
public sealed class AdapterConnectionFactory : IAdapterConnectionFactory
{
    private readonly IIpcFrameCodec codec;
    private readonly IAdapterConnectionLifecycle lifecycle;
    private readonly IAdapterPeerProofVerifier peerProofVerifier;
    private readonly IAdapterTrustAdminRequestHandler trustAdminRequestHandler;
    private readonly OwnerLifetimeId ownerLifetimeId;
    private readonly IClock clock;

    /// <summary>Creates a factory over the Host-lifetime singletons every accepted adapter connection shares.</summary>
    /// <param name="codec">Encodes and decodes IPC frames.</param>
    /// <param name="lifecycle">Publishes this adapter connection's availability transitions.</param>
    /// <param name="peerProofVerifier">Verifies a connecting adapter's peer-ownership proof.</param>
    /// <param name="trustAdminRequestHandler">Answers adapter-originated trust-admin IPC requests.</param>
    /// <param name="ownerLifetimeId">The owning Skyrim process's lifetime identity, verified against every accepted connection.</param>
    /// <param name="clock">The time source every connection reports through.</param>
    public AdapterConnectionFactory(
        IIpcFrameCodec codec,
        IAdapterConnectionLifecycle lifecycle,
        IAdapterPeerProofVerifier peerProofVerifier,
        IAdapterTrustAdminRequestHandler trustAdminRequestHandler,
        OwnerLifetimeId ownerLifetimeId,
        IClock clock)
    {
        this.codec = codec;
        this.lifecycle = lifecycle;
        this.peerProofVerifier = peerProofVerifier;
        this.trustAdminRequestHandler = trustAdminRequestHandler;
        this.ownerLifetimeId = ownerLifetimeId;
        this.clock = clock;
    }

    /// <inheritdoc/>
    public IAdapterIpcConnection Create(Stream stream) =>
        new AdapterIpcConnection(
            stream, codec, new AdapterIpcSession(lifecycle, peerProofVerifier, trustAdminRequestHandler, ownerLifetimeId), clock);
}
