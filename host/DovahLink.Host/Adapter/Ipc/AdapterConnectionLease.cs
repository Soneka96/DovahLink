using DovahLink.Host.Identity;

namespace DovahLink.Host.Adapter.Ipc;

/// <summary>
/// Identifies one exact logical Adapter connection lifetime -- the unit of identity
/// <see cref="IAdapterConnectionLifecycle"/> activates and deactivates. Every
/// <see cref="AdapterIpcSession"/> creates its own fresh lease for its own connection attempt and
/// never reuses one across a reconnect, so a caller still holding an old lease can never be mistaken
/// for the currently active connection, even when a newer lease shares the same
/// <see cref="AdapterInstanceId"/>. Deliberately a plain mutable class, not a record: its identity is
/// reference equality, never structural equality, and <see cref="IAdapterConnectionLifecycle"/> is
/// the only intended writer of its properties.
/// </summary>
public sealed class AdapterConnectionLease
{
    /// <summary>The connecting adapter's instance identity, set once by <see cref="IAdapterConnectionLifecycle.Activate"/>.</summary>
    public AdapterInstanceId InstanceId { get; internal set; }

    /// <summary>The connection generation assigned by <see cref="IAdapterConnectionLifecycle.Activate"/>.</summary>
    public long Generation { get; internal set; }

    /// <summary>Whether this lease is currently the lifecycle's active connection.</summary>
    public bool Active { get; internal set; }
}
