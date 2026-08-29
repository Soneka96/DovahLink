namespace DovahLink.Host.Adapter;

/// <summary>Opaque authorization for one adapter connection's resynchronization baseline.</summary>
public interface IAdapterResynchronizationToken
{
}

/// <summary>Host-created token that authorizes baselines only for its originating connection.</summary>
internal sealed class AdapterResynchronizationToken : IAdapterResynchronizationToken
{
}
