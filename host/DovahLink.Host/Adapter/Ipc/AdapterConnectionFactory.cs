using DovahLink.Host.PlayContext;
using DovahLink.Host.Process;
using DovahLink.Host.State;
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
    /// <summary>Encodes and decodes IPC frames.</summary>
    private readonly IIpcFrameCodec codec;

    /// <summary>Publishes this adapter connection's availability transitions.</summary>
    private readonly IAdapterConnectionLifecycle lifecycle;

    /// <summary>Verifies a connecting adapter's peer-ownership proof.</summary>
    private readonly IAdapterPeerProofVerifier peerProofVerifier;

    /// <summary>Answers adapter-originated trust-admin IPC requests.</summary>
    private readonly IAdapterTrustAdminRequestHandler trustAdminRequestHandler;

    /// <summary>The Host-lifetime tracker every connection's session notifies of adapter-reported play-context transitions.</summary>
    private readonly IPlayContextTracker playContextTracker;

    /// <summary>The Host-lifetime sink every connection's session routes decoded capture results into.</summary>
    private readonly ILiveCaptureSink liveCaptureSink;

    /// <summary>The Host-lifetime coordinator every connection's session reports its own resynchronize request's wire-level admission result to.</summary>
    private readonly IResynchronizationTransactionCoordinator resynchronizationTransactionCoordinator;

    /// <summary>The one Host-catalog plan reused by every accepted Adapter connection.</summary>
    private readonly ResynchronizationPlan resynchronizationPlan;

    /// <summary>This Host process's identity, whose <see cref="HostInstanceOptions.OwnerLifetimeId"/> is verified against every accepted connection.</summary>
    private readonly HostInstanceOptions hostInstance;

    /// <summary>The time source every connection reports through.</summary>
    private readonly IClock clock;

    /// <summary>Creates a factory over the Host-lifetime singletons every accepted adapter connection shares.</summary>
    /// <param name="codec">Encodes and decodes IPC frames.</param>
    /// <param name="lifecycle">Publishes this adapter connection's availability transitions.</param>
    /// <param name="peerProofVerifier">Verifies a connecting adapter's peer-ownership proof.</param>
    /// <param name="trustAdminRequestHandler">Answers adapter-originated trust-admin IPC requests.</param>
    /// <param name="playContextTracker">The Host-lifetime tracker every connection's session notifies of adapter-reported play-context transitions.</param>
    /// <param name="liveCaptureSink">The Host-lifetime sink every connection's session routes decoded capture results into.</param>
    /// <param name="resynchronizationTransactionCoordinator">The Host-lifetime coordinator every connection's session reports its own resynchronize request's wire-level admission result to.</param>
    /// <param name="resynchronizationPlan">The bounded native intent plan derived from the Host's live-state catalog.</param>
    /// <param name="hostInstance">This Host process's identity, whose <see cref="HostInstanceOptions.OwnerLifetimeId"/> is verified against every accepted connection.</param>
    /// <param name="clock">The time source every connection reports through.</param>
    public AdapterConnectionFactory(
        IIpcFrameCodec codec,
        IAdapterConnectionLifecycle lifecycle,
        IAdapterPeerProofVerifier peerProofVerifier,
        IAdapterTrustAdminRequestHandler trustAdminRequestHandler,
        IPlayContextTracker playContextTracker,
        ILiveCaptureSink liveCaptureSink,
        IResynchronizationTransactionCoordinator resynchronizationTransactionCoordinator,
        ResynchronizationPlan resynchronizationPlan,
        HostInstanceOptions hostInstance,
        IClock clock)
    {
        this.codec = codec;
        this.lifecycle = lifecycle;
        this.peerProofVerifier = peerProofVerifier;
        this.trustAdminRequestHandler = trustAdminRequestHandler;
        this.playContextTracker = playContextTracker;
        this.liveCaptureSink = liveCaptureSink;
        this.resynchronizationTransactionCoordinator = resynchronizationTransactionCoordinator;
        this.resynchronizationPlan = resynchronizationPlan;
        this.hostInstance = hostInstance;
        this.clock = clock;
    }

    /// <inheritdoc/>
    public IAdapterIpcConnection Create(Stream stream) =>
        new AdapterIpcConnection(
            stream,
            codec,
            new AdapterIpcSession(
                lifecycle, peerProofVerifier, trustAdminRequestHandler, playContextTracker, liveCaptureSink,
                resynchronizationTransactionCoordinator, resynchronizationPlan, hostInstance.OwnerLifetimeId),
            clock);
}
