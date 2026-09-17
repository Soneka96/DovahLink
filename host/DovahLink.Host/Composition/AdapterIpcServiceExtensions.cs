using DovahLink.Host.Adapter;
using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Client.Dispatch;
using DovahLink.Host.Process;
using DovahLink.Host.Time;
using Microsoft.Extensions.DependencyInjection;

namespace DovahLink.Host.Composition;

/// <summary>Registers the private adapter-IPC boundary.</summary>
public static class AdapterIpcServiceExtensions
{
    /// <summary>
    /// Registers the adapter-IPC listener and its collaborators. Every accepted adapter connection
    /// gets its own <see cref="AdapterIpcConnection"/>/<see cref="AdapterIpcSession"/> pair, built
    /// fresh by the composed <see cref="IAdapterConnectionFactory"/> -- connection-scoped, never
    /// shared across two accepted connections. Requires
    /// <see cref="CoreServiceExtensions.AddCoreServices"/> and
    /// <see cref="TrustServiceExtensions.AddTrustServices"/> to already be registered on
    /// <paramref name="services"/>.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="listenerPort">The private adapter-IPC loopback port to bind, or zero to let the operating system assign one.</param>
    /// <param name="ownerLifetimeId">The owning Skyrim process's lifetime identity, verified against every accepted connection.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddAdapterIpcServices(this IServiceCollection services, int listenerPort, OwnerLifetimeId ownerLifetimeId)
    {
        services.AddSingleton(new HostInstanceOptions(ownerLifetimeId));
        services.AddSingleton(new AdapterIpcOptions(listenerPort));

        services.AddSingleton<IAdapterConnectionLifecycle, AdapterConnectionLifecycle>();
        services.AddSingleton<IAdapterPeerProofVerifier, AdapterPeerProofVerifier>();
        services.AddSingleton<IIpcFrameCodec, IpcFrameCodec>();
        services.AddSingleton<IAdapterConnectionFactory, AdapterConnectionFactory>();
        services.AddSingleton<IAdapterIpcListener, AdapterIpcListener>();
        services.AddSingleton<IPairingAdapterNotifier, AdapterPairingNotifier>();
        // No real state-area decoder exists yet; this placeholder reflects this composition
        // root's actual current behavior until a later phase registers the real sink.
        services.AddSingleton<ILiveCaptureSink>(NullLiveCaptureSink.Instance);

        return services;
    }

    /// <summary>
    /// A composition-time placeholder for <see cref="ILiveCaptureSink"/> that discards every
    /// capture result. Correct today's composition root's production behavior, since no
    /// state-area decoder or authoritative-state application exists yet.
    /// </summary>
    private sealed class NullLiveCaptureSink : ILiveCaptureSink
    {
        /// <summary>The shared, stateless instance every connection routes through.</summary>
        public static readonly NullLiveCaptureSink Instance = new();

        /// <inheritdoc/>
        public void ApplyCaptureResult(IpcCaptureResultMessage captureResult)
        {
        }
    }
}
