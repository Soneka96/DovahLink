using System.Text;

namespace DovahLink.Host.Identity;

/// <summary>Provides the persistent Host ID together with the current operating-system computer name.</summary>
public interface IHostIdentityProvider
{
    /// <summary>Loads this Host's stable ID and reads its current computer name.</summary>
    /// <returns>A complete Host identity snapshot for the current startup.</returns>
    HostIdentity GetCurrent();
}

/// <inheritdoc cref="IHostIdentityProvider"/>
public sealed class HostIdentityProvider : IHostIdentityProvider
{
    /// <summary>The persistent store for the Host installation ID.</summary>
    private readonly IHostIdentityStore identityStore;

    /// <summary>The source of the operating-system computer name.</summary>
    private readonly IHostMachineNameProvider machineNameProvider;

    /// <summary>Creates a provider over the persistent ID store and current-name source.</summary>
    /// <param name="identityStore">The persistence boundary for the Host ID.</param>
    /// <param name="machineNameProvider">The operating-system computer-name source.</param>
    public HostIdentityProvider(IHostIdentityStore identityStore, IHostMachineNameProvider machineNameProvider)
    {
        this.identityStore = identityStore;
        this.machineNameProvider = machineNameProvider;
    }

    /// <inheritdoc/>
    public HostIdentity GetCurrent()
    {
        string hostName;
        try
        {
            hostName = machineNameProvider.GetMachineName();
        }
        catch (Exception)
        {
            hostName = string.Empty;
        }

        if (string.IsNullOrWhiteSpace(hostName) ||
            hostName.Any(char.IsControl) ||
            Encoding.UTF8.GetByteCount(hostName) > Constants.MaxDisplayNameLengthBytes)
        {
            hostName = "Skyrim PC";
        }

        return new HostIdentity(identityStore.LoadOrCreate(), hostName);
    }
}
